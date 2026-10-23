using System.Collections.Frozen;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Renders stable OpenAPI-style operation contracts by binding operation identity and deterministic URL metadata onto the
/// already-rendered response objects. This stays intentionally narrower than path/document assembly so later slices can
/// group operations by URL without rediscovering descriptor or response facts.
/// </summary>
/// <param name="ResponseObjects">The previously rendered response-object context that owns operation ordering.</param>
/// <param name="Operations">The rendered OpenAPI-style operations for later path/document assembly.</param>
public sealed record OpenApiOperationRenderingContext(
    OpenApiResponseObjectRenderingContext ResponseObjects,
    ImmutableArray<OpenApiOperationModel> Operations)
{
    /// <summary>
    /// Creates deterministic OpenAPI-style operation models from the supplied response-object rendering context.
    /// </summary>
    /// <param name="responseObjects">The response-object rendering context to compose with operation identity and URLs.</param>
    /// <returns>The rendered OpenAPI operation context.</returns>
    public static OpenApiOperationRenderingContext Create(OpenApiResponseObjectRenderingContext responseObjects)
    {
        return Create(responseObjects, OpenApiRequestBodyNaming.OperationName, OpenApiParameterNaming.BindingName);
    }

    /// <summary>
    /// Creates deterministic OpenAPI-style operation models from the supplied response-object rendering context, keying
    /// each request body as <paramref name="requestBodyNaming"/> says and naming query parameters as
    /// <paramref name="queryParameterNaming"/> says.
    /// </summary>
    /// <param name="responseObjects">The response-object rendering context to compose with operation identity and URLs.</param>
    /// <param name="requestBodyNaming">How request body schema components are keyed.</param>
    /// <param name="queryParameterNaming">How query parameters are named.</param>
    /// <returns>The rendered OpenAPI operation context.</returns>
    internal static OpenApiOperationRenderingContext Create(
        OpenApiResponseObjectRenderingContext responseObjects,
        OpenApiRequestBodyNaming requestBodyNaming,
        OpenApiParameterNaming queryParameterNaming)
    {
        ArgumentNullException.ThrowIfNull(responseObjects);

        var schemaProjection = responseObjects.SchemaComponents.SchemaProjection;
        var requestBodyNamesByType = GetRequestBodyNamesByOperationType(responseObjects.Operations, requestBodyNaming, schemaProjection.Naming);
        var warnings = new List<string>();
        var operations = responseObjects.Operations
            .Select(operation => CreateOperation(operation, schemaProjection, requestBodyNaming, queryParameterNaming, requestBodyNamesByType, warnings))
            .ToImmutableArray();

        return new OpenApiOperationRenderingContext(responseObjects, operations)
        {
            Warnings = [.. warnings],
        };
    }

    /// <summary>
    /// Applies consumer-provided per-operation processors, returning a new context with the modified operations.
    /// Each processor receives the current operation model and the source descriptor and may return a modified model
    /// (e.g. with parameters stripped or summaries overridden).
    /// </summary>
    /// <param name="app">The Blueprint app whose descriptors provide the source metadata for each operation.</param>
    /// <param name="options">The operation processor options containing the ordered processor list.</param>
    /// <returns>A new rendering context with the processed operations.</returns>
    public OpenApiOperationRenderingContext ApplyProcessors(BlueprintApp app, OpenApiOperationProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Processors.Count == 0)
        {
            return this;
        }

        var descriptorsByName = app.Operations.ToImmutableDictionary(
            static d => d.Name,
            static d => d,
            StringComparer.OrdinalIgnoreCase);

        var isLegacy = this.ResponseObjects.SchemaComponents.SchemaProjection.Naming == OpenApiSchemaNaming.Legacy;

        var processed = this.Operations
            .Select(operation =>
            {
                if (!descriptorsByName.TryGetValue(operation.OperationName, out var descriptor))
                {
                    return operation;
                }

                var current = operation;

                foreach (var processor in options.Processors)
                {
                    current = processor(current, descriptor);
                }

                // R182: legacy's ConfigureOperation ran after the body's schema was registered, edited that schema in place
                // and could then drop the body, which left the component, emptied, as R178's is for an operation with none.
                if (isLegacy
                    && operation.RequestBody is { } originalBody
                    && current is { RequestBody: null, BodylessRequestSchemaName: null })
                {
                    current = current with
                    {
                        BodylessRequestSchemaName = originalBody.SchemaName ?? operation.OperationName,
                        BodylessRequestSchemaDescription = originalBody.Description,
                    };
                }

                return current;
            })
            .ToImmutableArray();

        return this with { Operations = processed };
    }

    /// <summary>
    /// Returns a new context where every operation that lacks a body-bearing <c>200</c> response
    /// gains a synthetic one mirroring the first body-bearing <c>2xx</c> success response (e.g.
    /// <c>201 Created</c> or <c>202 Accepted</c>). The original semantic status codes are preserved.
    /// Bodyless success responses (<c>204 No Content</c>) are never aliased.
    /// </summary>
    /// <remarks>
    /// This exists for backward compatibility with TypeScript code generators that hardcode
    /// <c>responses['200']</c> when determining the return type of an operation. Without this
    /// normalization, those generators would treat <c>ResourceEventResult&lt;T&gt;</c> (201) as <c>void</c>.
    /// </remarks>
    /// <returns>A new context with synthetic <c>200</c> response aliases where applicable.</returns>
    public OpenApiOperationRenderingContext NormalizeSuccessResponsesTo200()
    {
        var normalized = this.Operations
            .Select(static operation =>
            {
                var responses = operation.Responses;

                // Already has a body-bearing 200 — nothing to do.
                if (responses.Any(static r => r.StatusCode == 200 && r.HasResponseBody))
                {
                    return operation;
                }

                // Find the first body-bearing 2xx success response to alias.
                var donor = responses.FirstOrDefault(static r =>
                    r.StatusCode is > 200 and < 300
                    && r.HasResponseBody
                    && !r.Content.IsEmpty);

                if (donor is null)
                {
                    return operation;
                }

                // Insert a synthetic 200 clone at the front (before the original 2xx).
                var synthetic200 = donor with { StatusCode = 200 };
                var updatedResponses = responses.Insert(0, synthetic200);

                return operation with { Responses = updatedResponses };
            })
            .ToImmutableArray();

        return this with { Operations = normalized };
    }

    private static OpenApiOperationModel CreateOperation(
        OpenApiResponseObjectOperationContext operation,
        OpenApiSchemaProjectionContext schemaProjection,
        OpenApiRequestBodyNaming requestBodyNaming,
        OpenApiParameterNaming queryParameterNaming,
        FrozenDictionary<Type, string> requestBodyNamesByType,
        List<string> warnings)
    {
        var httpMethods = operation.Operation.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var httpMethodMetadata)
            ? httpMethodMetadata.HttpMethods
                .OrderBy(static httpMethod => httpMethod, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
        var urlFormats = operation.Operation.Urls
            .Select(static url => ToPathKey(url.UrlFormat))
            .OrderBy(static urlFormat => urlFormat, StringComparer.Ordinal)
            .ToImmutableArray();
        var tags = operation.Operation.TryGetFeatureData<OperationGroupMetadataFeatureData>(out var operationGroupMetadata)
            && string.IsNullOrWhiteSpace(operationGroupMetadata.GroupName) == false
            ? ImmutableArray.Create(operationGroupMetadata.GroupName)
            : ImmutableArray<string>.Empty;
        var tagDescriptions = operationGroupMetadata?.GroupDescription is { Length: > 0 } desc
            ? ImmutableDictionary<string, string>.Empty.Add(operationGroupMetadata.GroupName, desc)
            : ImmutableDictionary<string, string>.Empty;

        var naming = schemaProjection.Naming;
        var bodyTypes = new RequestBodyTypeResolver(schemaProjection, requestBodyNamesByType, warnings);
        var (parameters, requestBody) = ProjectInputMetadata(operation.Operation, naming, schemaProjection.SchemaNamesByType, queryParameterNaming, schemaProjection.RequiredProperties, bodyTypes);

        if (requestBody is not null && requestBodyNaming == OpenApiRequestBodyNaming.OperationType)
        {
            requestBody = requestBody with { SchemaName = OpenApiSchemaNames.Format(operation.Operation.OperationType, naming) };
        }

        // R178: legacy registered the operation type's schema as it generated it and only then dropped an empty body, so a
        // non-GET operation with no body still had its (empty) component; a GET never had a body schema generated at all.
        // R184: it generated per method, so an operation published as GET and another method had it from the other.
        string? bodylessSchemaName = null;
        string? bodylessSchemaDescription = null;

        if (naming == OpenApiSchemaNaming.Legacy
            && requestBody is null
            && httpMethods.Any(static method => !string.Equals(method, "GET", StringComparison.Ordinal)))
        {
            bodylessSchemaName = requestBodyNaming == OpenApiRequestBodyNaming.OperationType
                ? OpenApiSchemaNames.Format(operation.Operation.OperationType, naming)
                : operation.Operation.Name;
            bodylessSchemaDescription = OpenApiSchemaProjectionContext.GetTypeDescription(operation.Operation.OperationType);
        }

        // Extract [Description] from the operation type for the operation-level summary.
        // Fall back to compile-time XML documentation when no [Description] attribute is present.
        var operationDescription = operation.Operation.OperationType
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;

        // Use XML <remarks> as the longer-form OpenAPI description when available.
        string? operationRemarks = null;

        if (operationDescription is null
            && operation.Operation.TryGetFeatureData<OperationDocumentationFeatureData>(out var docData))
        {
            operationDescription = docData.Summary;
            operationRemarks = docData.Remarks;
        }
        else if (operation.Operation.TryGetFeatureData<OperationDocumentationFeatureData>(out docData))
        {
            operationRemarks = docData.Remarks;
        }

        // R157: legacy wrote both, read at run time from the XML documentation beside the operation's assembly as it read a
        // parameter's, and the empty string where the operation has none. Where the documentation file is not deployed
        // the summary and remarks above are kept, since they are the same comments read at compile time.
        if (naming == OpenApiSchemaNaming.Legacy)
        {
            operationDescription = OpenApiXmlDocs.GetOperationSummary(operation.Operation.OperationType) ?? operationDescription ?? string.Empty;
            operationRemarks = OpenApiXmlDocs.GetOperationRemarks(operation.Operation.OperationType) ?? operationRemarks ?? string.Empty;
        }

        var obsoleteAttr = operation.Operation.OperationType.GetCustomAttribute<ObsoleteAttribute>(inherit: false);
        var isDeprecated = obsoleteAttr is not null;
        var deprecationReason = obsoleteAttr?.Message;

        var requiresAuthentication = operation.Operation.TryGetFeatureData<OperationAuthenticationFeatureData>(out var authData)
            && authData.Requirement == AuthenticationRequirement.Authenticated;

        return new OpenApiOperationModel(
            operation.Operation.Name,
            operation.Operation.OperationType.FullName ?? operation.Operation.OperationType.Name,
            httpMethods,
            urlFormats,
            tags,
            operation.Responses,
            parameters,
            requestBody)
        {
            BodylessRequestSchemaName = bodylessSchemaName,
            BodylessRequestSchemaDescription = bodylessSchemaDescription,
            Summary = operationDescription,
            Description = operationRemarks,
            Deprecated = isDeprecated,
            DeprecationReason = deprecationReason,
            RequiresAuthentication = requiresAuthentication,
            TagDescriptions = tagDescriptions,
        };
    }

    /// <summary>
    /// Returns the name of a query parameter: its binding name, or, when parameters are named by their property, the property's
    /// name; and under <see cref="OpenApiSchemaNaming.Legacy"/>, as legacy named a parameter by the name its descriptor gave it,
    /// the binding name when that is an explicit alias (<c>[FromQuery("validationToken")]</c>, R159), which the request binds
    /// by and the property's name does not reach. A binding name that differs from the property's only by case is no alias,
    /// as the host binds a query parameter whatever its case.
    /// </summary>
    private static string GetQueryParameterName(OperationPropertyBindingEntry entry, OpenApiParameterNaming queryParameterNaming, OpenApiSchemaNaming naming)
    {
        if (queryParameterNaming != OpenApiParameterNaming.PropertyName)
        {
            return entry.BindingName ?? entry.PropertyName;
        }

        return naming == OpenApiSchemaNaming.Legacy
            && entry.BindingName is { } bindingName
            && !string.Equals(bindingName, entry.PropertyName, StringComparison.OrdinalIgnoreCase)
                ? bindingName
                : entry.PropertyName;
    }

    /// <summary>
    /// Returns the key a route template is written under in the document's <c>paths</c>: the template with exactly one
    /// leading slash, as legacy keyed it (<c>"/" + route.RoutingUrl</c>, R149), whether the link that declares it wrote one.
    /// </summary>
    /// <remarks>
    /// This is applied where an operation's URL formats are read, before paths are grouped, so a route declared with and
    /// without its slash is one path rather than two keys of which one is invalid.
    /// </remarks>
    /// <param name="urlFormat">The route template as its link declares it.</param>
    /// <returns>The path key.</returns>
    internal static string ToPathKey(string urlFormat)
    {
        return "/" + urlFormat.TrimStart('/');
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "OpenAPI projection reflects over operation type properties that are preserved by the source generator's generated pipeline code.")]
    private static (ImmutableArray<OpenApiParameterModel> Parameters, OpenApiRequestBodyModel? RequestBody)
        ProjectInputMetadata(
            ApiOperationDescriptor descriptor,
            OpenApiSchemaNaming naming,
            FrozenDictionary<Type, string> schemaNamesByType,
            OpenApiParameterNaming queryParameterNaming,
            OpenApiRequiredProperties requiredProperties,
            RequestBodyTypeResolver bodyTypes)
    {
        if (!descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData))
        {
            return (ImmutableArray<OpenApiParameterModel>.Empty, null);
        }

        descriptor.TryGetFeatureData<OperationDocumentationFeatureData>(out var docData);

        // Pre-compute which route parameter names are catch-all (contain slashes/reserved chars).
        var catchAllNames = BuildCatchAllParameterNames(descriptor);

        var parameters = ImmutableArray.CreateBuilder<OpenApiParameterModel>();
        var bodyProperties = ImmutableArray.CreateBuilder<OpenApiRequestBodyPropertyModel>();

        // Conditional requirements are cross-property, so they are gathered against CLR names while the ownership plan
        // is walked and resolved to JSON names afterwards, once every body property is known.
        var conditionalRequirements = ImmutableArray.CreateBuilder<OpenApiConditionalRequirement>();
        var bodyPropertyNamesByClrName = new Dictionary<string, string>(StringComparer.Ordinal);
        var nullabilityContext = new NullabilityInfoContext();

        foreach (var entry in ownershipData.Entries)
        {
            var clrProperty = descriptor.OperationType.GetProperty(
                entry.PropertyName,
                BindingFlags.Public | BindingFlags.Instance);

            // Prefer CLR type-based mapping when property info is available for accurate collection/dictionary support.
            var (schemaType, schemaFormat) = clrProperty is not null
                ? MapClrTypeToOpenApiSchema(clrProperty.PropertyType)
                : MapClrTypeToOpenApiSchema(entry.PropertyTypeName);
            var constraints = OpenApiValidationConstraints.ExtractFromProperty(clrProperty, includeComparisonBounds: naming != OpenApiSchemaNaming.Legacy);

            // R44: legacy generated a parameter's schema by reflection alone (generator.Generate(property.PropertyType)),
            // never applying its validation-attribute rules there — those applied only to a request-body schema's own
            // property (NJsonSchema's JsonSchemaGenerator.GenerateWithReferenceAndNullability). Signature365's committed
            // Partners document's Page/PageSize query parameters, for example, are a bare {"type","format"} despite the
            // properties carrying range attributes. A parameter's own "required" flag (isRequired, below) is unaffected —
            // legacy did reflect that. Under Legacy naming a parameter drops every schema-level validation keyword;
            // Default keeps them, since they are real, useful constraints legacy simply could not describe there.
            var parameterConstraints = naming == OpenApiSchemaNaming.Legacy
                ? OpenApiValidationConstraints.None
                : constraints;

            // Legacy required every property whose type is not nullable, and every [Required] one, which it then did not
            // document as nullable. A route parameter is required either way.
            var isLegacyRule = requiredProperties == OpenApiRequiredProperties.NonNullable;
            var isRequired = constraints.IsRequired
                || (isLegacyRule && clrProperty is not null && OpenApiNullability.IsNotNullable(clrProperty, nullabilityContext));
            var isNullable = entry.IsNullable && !(isLegacyRule && constraints.IsRequired);
            var description = GetPropertyDescription(clrProperty, entry.PropertyName, docData, isBodyProperty: entry.Source == PropertyBindingSource.Body);
            var enumValues = GetEnumValues(clrProperty);
            var enumSchemaRef = GetEnumSchemaRef(clrProperty, naming, schemaNamesByType);

            // R159: legacy's schema for an enum parameter carried the enum's own description, which the generated client reads.
            var parameterEnumDescription = naming == OpenApiSchemaNaming.Legacy && enumSchemaRef is not null && clrProperty is not null
                ? OpenApiSchemaProjectionContext.GetTypeDescription(Nullable.GetUnderlyingType(clrProperty.PropertyType) ?? clrProperty.PropertyType)
                : null;

            // Validation-derived format (email, uri, phone) takes precedence over CLR type format.
            // R183: under Legacy only a format legacy's NJsonSchema derived (OpenApiValidationConstraints.GetFormat).
            var validationFormat = naming == OpenApiSchemaNaming.Legacy
                ? OpenApiValidationConstraints.GetFormat(clrProperty, useLegacyRules: true)
                : constraints.Format;
            var effectiveFormat = validationFormat ?? schemaFormat;

            // R132: a parameter's validation-derived format is one legacy never wrote either (the same reflection-only schema as
            // R44); its type's format (int32, int64, date-time) is the only one Signature365's 811 committed parameters carry.
            var parameterFormat = parameterConstraints.Format ?? schemaFormat;

            var obsoletePropertyAttr = clrProperty?.GetCustomAttribute<System.ObsoleteAttribute>();
            var isDeprecated = obsoletePropertyAttr is not null;
            var propertyDeprecationReason = obsoletePropertyAttr?.Message;
            var defaultValueAttr = clrProperty?.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>();
            var exampleValueAttr = clrProperty?.GetCustomAttribute<Blueprint.Model.Attributes.OpenApiExampleAttribute>();
            var isWriteOnly = clrProperty?.GetCustomAttribute<Blueprint.Model.Attributes.OpenApiWriteOnlyAttribute>() is not null;

            // Legacy gave each parameter a root schema, which NJsonSchema titled with its schema name generator: the name
            // under legacy naming of the type a Nullable<T> wraps, which the writer passes through the schema name formatter
            // as legacy's BlueprintSchemaNameGenerator did.
            var schemaTitle = naming == OpenApiSchemaNaming.Legacy && clrProperty is not null
                ? OpenApiSchemaNames.Format(Nullable.GetUnderlyingType(clrProperty.PropertyType) ?? clrProperty.PropertyType, naming)
                : null;

            // Resolve array element schema for collection-typed query/header/cookie parameters
            string? paramItemsType = null;
            string? paramItemsFormat = null;
            var paramItemsEnumValues = ImmutableArray<string>.Empty;
            string? paramItemsEnumSchemaRef = null;
            string? paramItemsEnumDescription = null;
            if (schemaType == "array" && clrProperty is not null)
            {
                var (itemsType, itemsFormat, _, itemsEnumValues, _, _, _) = ResolveCollectionSchema(clrProperty, schemaNamesByType);
                paramItemsType = itemsType;
                paramItemsFormat = itemsFormat;
                paramItemsEnumValues = itemsEnumValues;

                // R148: legacy typed the items of an enum array parameter by the enum, which the generated client reads from the
                // last segment of a reference; inline items (Default's) leave it "Array<undefined>". Under Legacy the items
                // reference the enum's component, which the document writes for a parameter's enum too.
                if (naming == OpenApiSchemaNaming.Legacy
                    && !itemsEnumValues.IsDefaultOrEmpty
                    && GetCollectionElementType(clrProperty.PropertyType, dictionaryValues: false) is { } elementType)
                {
                    var enumType = Nullable.GetUnderlyingType(elementType) ?? elementType;
                    paramItemsEnumSchemaRef = GetEnumSchemaName(enumType, naming, schemaNamesByType);
                    paramItemsEnumDescription = OpenApiSchemaProjectionContext.GetTypeDescription(enumType);
                }
            }

            // R153: under Legacy naming an owned property inherited from a base type is in the body as well.
            var alsoInBody = entry.Source != PropertyBindingSource.Body && IsRequestBodyEntry(descriptor, entry, clrProperty, naming);
            var bodyPropertyName = alsoInBody
                ? System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(entry.PropertyName)
                : entry.BindingName ?? entry.PropertyName;
            var bodyDescription = alsoInBody
                ? GetPropertyDescription(clrProperty, entry.PropertyName, docData, isBodyProperty: true)
                : description;

            switch (entry.Source)
            {
                case PropertyBindingSource.Route:
                    var bindingName = entry.BindingName ?? entry.PropertyName;
                    parameters.Add(new OpenApiParameterModel(
                        bindingName,
                        "path",
                        Required: true,
                        schemaType,
                        parameterFormat)
                    {
                        Description = description,
                        MinLength = parameterConstraints.MinLength,
                        MaxLength = parameterConstraints.MaxLength,
                        Pattern = parameterConstraints.Pattern,
                        Minimum = parameterConstraints.Minimum,
                        Maximum = parameterConstraints.Maximum,
                        ExclusiveMinimum = parameterConstraints.ExclusiveMinimum,
                        ExclusiveMaximum = parameterConstraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                        SchemaTitle = schemaTitle,
                        EnumDescription = parameterEnumDescription,
                        AllowReserved = catchAllNames.Contains(bindingName),
                    });

                    if (alsoInBody)
                    {
                        goto case PropertyBindingSource.Body;
                    }

                    break;

                case PropertyBindingSource.Query:
                    parameters.Add(new OpenApiParameterModel(
                        GetQueryParameterName(entry, queryParameterNaming, naming),
                        "query",
                        Required: isRequired,
                        schemaType,
                        parameterFormat)
                    {
                        Description = description,
                        MinLength = parameterConstraints.MinLength,
                        MaxLength = parameterConstraints.MaxLength,
                        Pattern = parameterConstraints.Pattern,
                        Minimum = parameterConstraints.Minimum,
                        Maximum = parameterConstraints.Maximum,
                        ExclusiveMinimum = parameterConstraints.ExclusiveMinimum,
                        ExclusiveMaximum = parameterConstraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                        SchemaTitle = schemaTitle,
                        EnumDescription = parameterEnumDescription,
                        ItemsType = paramItemsType,
                        ItemsFormat = paramItemsFormat,
                        ItemsEnumValues = paramItemsEnumValues,
                        ItemsEnumSchemaRef = paramItemsEnumSchemaRef,
                        ItemsEnumDescription = paramItemsEnumDescription,
                    });

                    // R27: a query parameter of an undescribed complex type rendered bare ({"type": "object"}, no
                    // properties, no $ref — a parameter is never itself a component) with no warning naming it, unlike
                    // the equivalent request-body property.
                    if (schemaType == "object")
                    {
                        bodyTypes.WarnIfUndescribed(descriptor, entry.PropertyName, clrProperty?.PropertyType, isQueryParameter: true);
                    }

                    if (alsoInBody)
                    {
                        goto case PropertyBindingSource.Body;
                    }

                    break;

                case PropertyBindingSource.Header:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "header",
                        Required: isRequired,
                        schemaType,
                        parameterFormat)
                    {
                        Description = description,
                        MinLength = parameterConstraints.MinLength,
                        MaxLength = parameterConstraints.MaxLength,
                        Pattern = parameterConstraints.Pattern,
                        Minimum = parameterConstraints.Minimum,
                        Maximum = parameterConstraints.Maximum,
                        ExclusiveMinimum = parameterConstraints.ExclusiveMinimum,
                        ExclusiveMaximum = parameterConstraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                        SchemaTitle = schemaTitle,
                        EnumDescription = parameterEnumDescription,
                    });

                    if (alsoInBody)
                    {
                        goto case PropertyBindingSource.Body;
                    }

                    break;

                case PropertyBindingSource.Cookie:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "cookie",
                        Required: isRequired,
                        schemaType,
                        parameterFormat)
                    {
                        Description = description,
                        MinLength = parameterConstraints.MinLength,
                        MaxLength = parameterConstraints.MaxLength,
                        Pattern = parameterConstraints.Pattern,
                        Minimum = parameterConstraints.Minimum,
                        Maximum = parameterConstraints.Maximum,
                        ExclusiveMinimum = parameterConstraints.ExclusiveMinimum,
                        ExclusiveMaximum = parameterConstraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                        SchemaTitle = schemaTitle,
                        EnumDescription = parameterEnumDescription,
                    });

                    if (alsoInBody)
                    {
                        goto case PropertyBindingSource.Body;
                    }

                    break;

                case PropertyBindingSource.Body:
                    var (bodyItemsType, bodyItemsFormat, bodyItemsSchemaRef, _, bodyAdditionalType, bodyAdditionalFormat, bodyAdditionalSchemaRef) = ResolveCollectionSchema(clrProperty, schemaNamesByType);

                    // Legacy documented a dictionary's open value as any value, and a value as nullable when its
                    // annotation said so.
                    var bodyValueType = clrProperty is null ? null : OpenApiDictionaryValues.GetValueType(clrProperty.PropertyType);
                    var bodyValuesAreAnyValue = bodyValueType is not null && OpenApiDictionaryValues.IsAnyValue(bodyValueType);
                    var bodyValuesAreNullable = bodyValueType is not null
                        && !bodyValuesAreAnyValue
                        && OpenApiDictionaryValues.AreValuesNullable(clrProperty, bodyValueType, nullabilityContext);

                    bodyPropertyNamesByClrName[entry.PropertyName] = bodyPropertyName;
                    conditionalRequirements.AddRange(OpenApiValidationConstraints.ExtractConditionalRequirements(clrProperty, useLegacyRules: naming == OpenApiSchemaNaming.Legacy));

                    bodyProperties.Add(new OpenApiRequestBodyPropertyModel(
                        bodyPropertyName,
                        schemaType,
                        effectiveFormat)
                    {
                        Description = bodyDescription,
                        IsRequired = isRequired,
                        MinLength = OpenApiValidationConstraints.GetDocumentedMinLength(
                            constraints.MinLength,
                            constraints.RequiresNonEmptyString,
                            isNonEnumString: schemaType == "string" && enumValues.IsDefaultOrEmpty && enumSchemaRef is null,
                            hasStringLengthAttribute: constraints.HasStringLengthAttribute,
                            useLegacyStringLengthZero: naming == OpenApiSchemaNaming.Legacy),
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        EnumSchemaRef = enumSchemaRef,
                        IsNullable = isNullable,
                        ItemsType = bodyItemsType,
                        ItemsFormat = bodyItemsFormat,
                        ItemsSchemaRef = bodyItemsSchemaRef,
                        AdditionalPropertiesType = bodyAdditionalType,
                        AdditionalPropertiesFormat = bodyAdditionalFormat,
                        AdditionalPropertiesSchemaRef = bodyAdditionalSchemaRef,
                        AllowsAdditionalProperties = bodyValuesAreAnyValue,
                        AdditionalPropertiesAreNullable = bodyValuesAreNullable,

                        // A nullable value type element is nullable under either naming, whether or not it has a component;
                        // only a nullable reference annotation is legacy's.
                        ItemsAreNullable = OpenApiNullability.AreValueItemsNullable(clrProperty?.PropertyType)
                            || (naming == OpenApiSchemaNaming.Legacy
                                && bodyItemsSchemaRef is not null
                                && OpenApiNullability.AreItemsNullable(clrProperty, nullabilityContext)),
                        EnumDescription = enumSchemaRef is null || clrProperty is null
                            ? null
                            : OpenApiSchemaProjectionContext.GetTypeDescription(Nullable.GetUnderlyingType(clrProperty.PropertyType) ?? clrProperty.PropertyType),
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        DefaultValue = defaultValueAttr?.Value,
                        ExampleValue = exampleValueAttr?.Value,
                        IsWriteOnly = isWriteOnly,
                        SchemaRef = schemaType == "object" ? bodyTypes.FindSchemaName(descriptor, entry.PropertyName, clrProperty?.PropertyType) : null,
                    });

                    if (bodyItemsType == "object" && bodyItemsSchemaRef is null)
                    {
                        bodyTypes.WarnIfUndescribed(descriptor, entry.PropertyName, GetCollectionElementType(clrProperty?.PropertyType, dictionaryValues: false), isQueryParameter: false, collectionType: clrProperty?.PropertyType);
                    }

                    if (bodyAdditionalType == "object" && bodyAdditionalSchemaRef is null)
                    {
                        bodyTypes.WarnIfUndescribed(descriptor, entry.PropertyName, GetCollectionElementType(clrProperty?.PropertyType, dictionaryValues: true), isQueryParameter: false, collectionType: clrProperty?.PropertyType);
                    }

                    break;
            }
        }

        // R142: legacy listed parameters in the order of the operation's properties (the derived type's, then each base's,
        // route, query and header interleaved as declared), which Signature365's generated client turns into a method's
        // positional arguments. Default keeps its stable order by location and then name, which a consumer's own client may
        // already depend on. The ownership entries are already in declaration order.
        var orderedParameters = naming == OpenApiSchemaNaming.Legacy
            ? parameters.ToImmutable()
            : parameters
                .OrderBy(static p => p.In, StringComparer.Ordinal)
                .ThenBy(static p => p.Name, StringComparer.Ordinal)
                .ToImmutableArray();

        // R165: legacy's schema for the operation's type listed its properties as its contract did (OpenApiPropertyOrder),
        // which is not the order of the operation's own property list that the parameters follow.
        var orderedBodyProperties = naming == OpenApiSchemaNaming.Legacy
            ? OrderBodyPropertiesAsLegacy(bodyProperties.ToImmutable(), descriptor.OperationType, bodyPropertyNamesByClrName)
            : bodyProperties.ToImmutable();

        var requestBody = bodyProperties.Count > 0
            ? new OpenApiRequestBodyModel(Required: true, orderedBodyProperties)
            {
                Conditionals = ResolveBodyConditionals(conditionalRequirements, bodyPropertyNamesByClrName),

                // Legacy's NJsonSchema described the body's schema by the operation type, as it did any other.
                Description = OpenApiSchemaProjectionContext.GetTypeDescription(descriptor.OperationType) ?? docData?.Summary,
            }
            : null;

        return (orderedParameters, requestBody);
    }

    /// <summary>
    /// Puts the properties of an operation's request body in the order legacy's schema for the operation type listed them
    /// (<see cref="OpenApiPropertyOrder"/>), a property no CLR property is found for going last in the order it had.
    /// </summary>
    private static ImmutableArray<OpenApiRequestBodyPropertyModel> OrderBodyPropertiesAsLegacy(
        ImmutableArray<OpenApiRequestBodyPropertyModel> bodyProperties,
        Type operationType,
        Dictionary<string, string> bodyPropertyNamesByClrName)
    {
        var order = OpenApiPropertyOrder.GetLegacyOrder(operationType).Select(static (name, index) => (name, index)).ToDictionary(static pair => pair.name, static pair => pair.index, StringComparer.Ordinal);
        var clrNamesByBodyName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (clrName, bodyName) in bodyPropertyNamesByClrName)
        {
            clrNamesByBodyName.TryAdd(bodyName, clrName);
        }

        return
        [
            .. bodyProperties.OrderBy(property =>
                clrNamesByBodyName.TryGetValue(property.Name, out var clrName) && order.TryGetValue(clrName, out var position) ? position : int.MaxValue),
        ];
    }

    /// <summary>
    /// Resolves gathered conditional requirements from CLR property names to the JSON-visible body property names,
    /// dropping any whose target or dependent is not a body property of this operation.
    /// </summary>
    /// <remarks>
    /// A conditional is only expressible where both properties live in the same object schema. Route, query, header and
    /// cookie properties render into the operation's <c>parameters</c> array, which has no containing object, so a
    /// conditional touching one of them is skipped rather than mis-attached to the body schema. The body schema also
    /// renders <c>"additionalProperties": false</c>, which would make a condition naming an undeclared property
    /// unsatisfiable rather than merely unhelpful.
    /// </remarks>
    private static ImmutableArray<OpenApiConditionalRequirement> ResolveBodyConditionals(
        IReadOnlyList<OpenApiConditionalRequirement> conditionalRequirements,
        Dictionary<string, string> bodyPropertyNamesByClrName)
    {
        if (conditionalRequirements.Count == 0)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<OpenApiConditionalRequirement>(conditionalRequirements.Count);

        foreach (var requirement in conditionalRequirements)
        {
            if (bodyPropertyNamesByClrName.TryGetValue(requirement.TargetPropertyName, out var targetName)
                && bodyPropertyNamesByClrName.TryGetValue(requirement.DependentPropertyName, out var dependentName))
            {
                builder.Add(requirement with
                {
                    TargetPropertyName = targetName,
                    DependentPropertyName = dependentName,
                });
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Maps a fully-qualified CLR type name to the corresponding OpenAPI schema type and optional format.
    /// The supported types match the set validated by <c>PropertyOwnershipModule</c> at compile time.
    /// </summary>
    private static (string SchemaType, string? SchemaFormat) MapClrTypeToOpenApiSchema(string? clrTypeName)
    {
        return clrTypeName switch
        {
            "global::System.String" or "string" => ("string", null),
            "global::System.Int32" or "int" => ("integer", "int32"),
            "global::System.Int64" or "long" => ("integer", "int64"),
            "global::System.Int16" or "short" => ("integer", "int32"),
            "global::System.Byte" or "byte" => ("integer", "int32"),
            "global::System.SByte" => ("integer", "int32"),
            "global::System.UInt16" => ("integer", "int32"),
            "global::System.UInt32" => ("integer", "int64"),
            "global::System.UInt64" => ("integer", "int64"),
            "global::System.Boolean" or "bool" => ("boolean", null),
            "global::System.Decimal" or "decimal" => ("number", "double"),
            "global::System.Double" or "double" => ("number", "double"),
            "global::System.Single" or "float" => ("number", "float"),
            "global::System.Guid" => ("string", "uuid"),
            "global::System.DateTime" => ("string", "date-time"),
            "global::System.DateTimeOffset" => ("string", "date-time"),
            "global::System.DateOnly" => ("string", "date"),
            "global::System.TimeOnly" => ("string", "time"),
            "global::System.TimeSpan" => ("string", "duration"),
            "global::System.Uri" => ("string", "uri"),
            "global::System.Half" => ("number", "float"),
            _ => ("string", null),
        };
    }

    /// <summary>
    /// Extracts the <see cref="DescriptionAttribute"/> value from a property on the operation type,
    /// returning null when no description is specified.
    /// </summary>
    private static string? GetPropertyDescription(
        System.Reflection.PropertyInfo? property,
        string propertyName,
        OperationDocumentationFeatureData? docData,
        bool isBodyProperty)
    {
        var attributeDescription = property?
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;

        if (attributeDescription is not null)
        {
            return attributeDescription;
        }

        // Then the XML documentation file beside the declaring assembly, formatted as legacy formatted it: a body property
        // as its NJsonSchema described one, a parameter as its own XmlDocsExtensions did. It is read wherever the property
        // was declared, a base class of another assembly included.
        var documented = property is null
            ? null
            : isBodyProperty ? OpenApiXmlDocs.GetSchemaSummary(property) : OpenApiXmlDocs.GetParameterSummary(property);

        if (documented is not null)
        {
            return documented;
        }

        // Fall back to compile-time XML documentation when no documentation file is deployed
        if (docData?.PropertySummaries.TryGetValue(propertyName, out var xmlSummary) == true)
        {
            return xmlSummary;
        }

        return null;
    }

    /// <summary>
    /// Extracts enum values when a property's CLR type is an enum, returning the defined names
    /// as an immutable array for OpenAPI <c>enum</c> rendering.
    /// </summary>
    private static ImmutableArray<string> GetEnumValues(System.Reflection.PropertyInfo? property)
    {
        if (property is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (propertyType.IsEnum)
        {
            return Enum.GetNames(propertyType).ToImmutableArray();
        }

        // Project AllowedValuesAttribute string values as OpenAPI enum when the property is a string.
        if (propertyType == typeof(string))
        {
            var allowedValues = property.GetCustomAttributes(inherit: true)
                .OfType<AllowedValuesAttribute>()
                .FirstOrDefault();

            if (allowedValues?.Values is { Length: > 0 } values)
            {
                var stringValues = values
                    .OfType<string>()
                    .ToImmutableArray();

                if (stringValues.Length == values.Length)
                {
                    return stringValues;
                }
            }
        }

        return ImmutableArray<string>.Empty;
    }

    /// <summary>
    /// Returns the schema component name of a property's enum type, as the response schemas name it, or <c>null</c>
    /// when the property is not an enum. This name is used as the component schema key for <c>$ref</c>-based enum
    /// rendering in OpenAPI.
    /// </summary>
    private static string? GetEnumSchemaRef(System.Reflection.PropertyInfo? property, OpenApiSchemaNaming naming, FrozenDictionary<Type, string> schemaNamesByType)
    {
        if (property is null)
        {
            return null;
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (!propertyType.IsEnum)
        {
            return null;
        }

        return GetEnumSchemaName(propertyType, naming, schemaNamesByType);
    }

    /// <summary>Returns the schema component name of an enum type, as the response schemas name it.</summary>
    private static string GetEnumSchemaName(Type enumType, OpenApiSchemaNaming naming, FrozenDictionary<Type, string> schemaNamesByType)
    {
        // Prefer the name the schema projection's SchemaNamer already assigned this enum (shared collision-counter pool
        // with every object type): a query/route parameter enum also used in a body or response must reference the same
        // component, told apart from another type of the same simple name the same way. One reached nowhere else falls
        // back to the bare formatted name.
        return schemaNamesByType.TryGetValue(enumType, out var assignedName) ? assignedName : OpenApiSchemaNames.Format(enumType, naming);
    }

    /// <summary>
    /// Resolves collection/dictionary element schema information from a CLR property for OpenAPI rendering.
    /// Returns typed items info for arrays/lists and additionalProperties info for dictionaries.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved for operation types registered with the serializer context.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Type interfaces are preserved for operation types registered with the serializer context.")]
    private static (string? ItemsType, string? ItemsFormat, string? ItemsSchemaRef, ImmutableArray<string> ItemsEnumValues, string? AdditionalPropertiesType, string? AdditionalPropertiesFormat, string? AdditionalPropertiesSchemaRef) ResolveCollectionSchema(
        System.Reflection.PropertyInfo? property,
        FrozenDictionary<Type, string> schemaNamesByType)
    {
        if (property is null)
        {
            return (null, null, null, ImmutableArray<string>.Empty, null, null, null);
        }

        var propertyType = property.PropertyType;

        // string implements IEnumerable<char> — skip it to avoid emitting a spurious items schema.
        if (propertyType == typeof(string))
        {
            return (null, null, null, ImmutableArray<string>.Empty, null, null, null);
        }

        // Check for a dictionary first (dictionaries also implement IEnumerable). An open value is any value, which legacy
        // documented as {} and the body property records with AllowsAdditionalProperties.
        var valueType = OpenApiDictionaryValues.GetValueType(propertyType);
        if (valueType is not null)
        {
            if (OpenApiDictionaryValues.IsAnyValue(valueType))
            {
                return (null, null, null, ImmutableArray<string>.Empty, null, null, null);
            }

            var (additionalType, additionalFormat) = MapClrTypeToOpenApiSchema(valueType);

            // A complex value type with a component of its own is referenced; any other stays an inline object.
            return (null, null, null, ImmutableArray<string>.Empty, additionalType, additionalFormat, additionalType == "object" ? FindSchemaName(valueType, schemaNamesByType) : null);
        }

        // Check for IEnumerable<T>, which the type may be itself (R144: GetInterfaces does not list the type).
        var enumInterface = propertyType.GetInterfaces()
            .Append(propertyType)
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumInterface is not null)
        {
            var elementType = enumInterface.GetGenericArguments()[0];
            var (itemType, itemFormat) = MapClrTypeToOpenApiSchema(elementType);

            // An enum element carries no component of its own here (unlike a body property's items — R30a): legacy's
            // NJsonSchema still carried a collection query parameter's enum member names, however oddly nested (a
            // path-local "definitions" block), so this renders them inline on "items" rather than losing them to a bare
            // {"type":"string"}.
            var underlyingElementType = Nullable.GetUnderlyingType(elementType) ?? elementType;
            var itemsEnumValues = underlyingElementType.IsEnum
                ? Enum.GetNames(underlyingElementType).ToImmutableArray()
                : ImmutableArray<string>.Empty;

            // A complex element type with a component of its own is referenced; any other stays an inline object.
            return (itemType, itemFormat, itemType == "object" ? FindSchemaName(elementType, schemaNamesByType) : null, itemsEnumValues, null, null, null);
        }

        return (null, null, null, ImmutableArray<string>.Empty, null, null, null);
    }

    /// <summary>
    /// Gets what the operations' request bodies could not describe as legacy Blueprint did, each named for the consumer to
    /// act on: a property whose type the app's serializer context does not describe.
    /// </summary>
    internal ImmutableArray<string> Warnings { get; init; } = [];

    /// <summary>
    /// Whether <paramref name="entry"/> is a property of the operation's request body: one the ownership plan gives the body
    /// and, under <see cref="OpenApiSchemaNaming.Legacy"/>, a route, query, header or cookie property inherited from a
    /// base type of a non-GET operation.
    /// </summary>
    /// <param name="descriptor">The operation.</param>
    /// <param name="entry">The ownership plan's entry for one of its properties.</param>
    /// <param name="clrProperty">The property the entry is for, where it can be found on the operation type.</param>
    /// <param name="naming">How the document names schemas, which also says whether it reproduces legacy's.</param>
    /// <returns><see langword="true"/> when the property is part of the request body.</returns>
    /// <remarks>
    /// Legacy took an owned property out of a body schema by comparing <c>PropertyInfo</c> references
    /// (Signature365 <c>8b40ecc961</c>, <c>OpenApiDocumentBuilder.cs:461-463</c>), and Newtonsoft's
    /// <c>ReflectionUtils.GetProperties</c> replaces a property declared on a base type with the one it gets from the
    /// declaring type, which is never the one in the operation's own list. An inherited owned property was so never taken
    /// out (R153). A property the operation declares itself, and every property of a GET, which has no body, behave as
    /// they always did.
    /// </remarks>
    internal static bool IsRequestBodyEntry(
        ApiOperationDescriptor descriptor,
        OperationPropertyBindingEntry entry,
        PropertyInfo? clrProperty,
        OpenApiSchemaNaming naming)
    {
        if (entry.Source == PropertyBindingSource.Body)
        {
            return true;
        }

        return naming == OpenApiSchemaNaming.Legacy
            && entry.Source is PropertyBindingSource.Route or PropertyBindingSource.Query or PropertyBindingSource.Header or PropertyBindingSource.Cookie
            && clrProperty is not null
            && clrProperty.DeclaringType != descriptor.OperationType
            && !(descriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var httpMethods)
                 && httpMethods.HttpMethods.Any(static method => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Maps the CLR type of each operation that has a request body to the name of that body's schema component, as
    /// <see cref="OpenApiDocumentRenderingContext"/> will name it: the operation's type under
    /// <see cref="OpenApiRequestBodyNaming.OperationType"/>, otherwise its name.
    /// </summary>
    private static FrozenDictionary<Type, string> GetRequestBodyNamesByOperationType(
        ImmutableArray<OpenApiResponseObjectOperationContext> operations,
        OpenApiRequestBodyNaming requestBodyNaming,
        OpenApiSchemaNaming naming)
    {
        var names = new Dictionary<Type, string>();

        foreach (var operation in operations)
        {
            var descriptor = operation.Operation;

            if (descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownership)
                && ownership.Entries.Any(entry => IsRequestBodyEntry(
                    descriptor,
                    entry,
                    descriptor.OperationType.GetProperty(entry.PropertyName, BindingFlags.Public | BindingFlags.Instance),
                    naming)))
            {
                var name = requestBodyNaming == OpenApiRequestBodyNaming.OperationType
                    ? OpenApiSchemaNames.Format(descriptor.OperationType, naming)
                    : descriptor.Name;

                names.TryAdd(descriptor.OperationType, name);
            }
        }

        return names.ToFrozenDictionary();
    }

    /// <summary>
    /// Returns the type of a collection's items, or of a dictionary's values when <paramref name="dictionaryValues"/> is set.
    /// </summary>
    /// <param name="type">The collection's type, or <see langword="null"/> when it is not known.</param>
    /// <param name="dictionaryValues">Whether to return a dictionary's value type rather than a collection's item type.</param>
    /// <returns>The type of the items or values, or <see langword="null"/> when <paramref name="type"/> has none.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved for operation types registered with the serializer context.")]
    internal static Type? GetCollectionElementType(Type? type, bool dictionaryValues)
    {
        if (type is null)
        {
            return null;
        }

        if (dictionaryValues)
        {
            return OpenApiDictionaryValues.GetValueType(type);
        }

        var implemented = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return implemented?.GetGenericArguments()[0];
    }

    /// <summary>
    /// Returns the name of the schema component projected for <paramref name="type"/>, or <see langword="null"/> when it
    /// has none. A nullable struct takes its underlying type's.
    /// </summary>
    private static string? FindSchemaName(Type? type, FrozenDictionary<Type, string> schemaNamesByType)
    {
        if (type is null)
        {
            return null;
        }

        return schemaNamesByType.TryGetValue(Nullable.GetUnderlyingType(type) ?? type, out var name) ? name : null;
    }

    /// <summary>
    /// Maps a CLR <see cref="Type"/> to its OpenAPI schema type and optional format.
    /// Uses the actual type object rather than a type name string for accurate collection support.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved for operation types registered with the serializer context.")]
    private static (string SchemaType, string? SchemaFormat) MapClrTypeToOpenApiSchema(Type clrType)
    {
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (underlying == typeof(string))
        {
            return ("string", null);
        }

        // The serializer writes a byte array as a base64 string, and legacy documented it as one.
        if (underlying == typeof(byte[]))
        {
            return ("string", "byte");
        }

        // A System.Type is written as its name, so a property of one is a string, however the app's converter spells it (R145).
        if (typeof(Type).IsAssignableFrom(underlying))
        {
            return ("string", null);
        }

        if (underlying == typeof(int))
        {
            return ("integer", "int32");
        }

        if (underlying == typeof(long))
        {
            return ("integer", "int64");
        }

        if (underlying == typeof(short) || underlying == typeof(byte) || underlying == typeof(sbyte) || underlying == typeof(ushort))
        {
            return ("integer", "int32");
        }

        if (underlying == typeof(uint))
        {
            return ("integer", "int64");
        }

        if (underlying == typeof(ulong))
        {
            return ("integer", "int64");
        }

        if (underlying == typeof(bool))
        {
            return ("boolean", null);
        }

        if (underlying == typeof(decimal) || underlying == typeof(double))
        {
            return ("number", "double");
        }

        if (underlying == typeof(float))
        {
            return ("number", "float");
        }

        if (underlying == typeof(Guid))
        {
            return ("string", "uuid");
        }

        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
        {
            return ("string", "date-time");
        }

        if (underlying == typeof(DateOnly))
        {
            return ("string", "date");
        }

        if (underlying == typeof(TimeOnly))
        {
            return ("string", "time");
        }

        if (underlying == typeof(Uri))
        {
            return ("string", "uri");
        }

        if (underlying == typeof(TimeSpan))
        {
            return ("string", "duration");
        }

        if (underlying == typeof(Half))
        {
            return ("number", "float");
        }

        // NodaTime types serialise as ISO-8601 strings via NodaTime.Serialization.SystemTextJson. Matched by full name,
        // as OpenApiSchemaProjectionContext.MapFormat already does for a response-schema property, so Blueprint.Runtime
        // does not need a NodaTime package reference. Without this, a request-body Instant (etc.) property falls
        // through to the generic string-convertible-type branch below and loses its format entirely (R45) — legacy's
        // committed Portal document gives ExtendLoggingSessionCommand.newExpirationDate (an Instant)
        // {"type":"string","format":"date-time"}, and the response-schema path already gets this right for the same
        // CLR type, so this is a mapping gap in this path specifically, not a legacy-only quirk.
        var nodaTimeFullName = underlying.FullName;
        if (nodaTimeFullName is not null && nodaTimeFullName.StartsWith("NodaTime.", StringComparison.Ordinal))
        {
            var nodaTimeFormat = nodaTimeFullName switch
            {
                "NodaTime.Instant" => "date-time",
                "NodaTime.OffsetDateTime" => "date-time",
                "NodaTime.ZonedDateTime" => "date-time",
                "NodaTime.LocalDateTime" => "date-time",
                "NodaTime.LocalDate" => "date",
                "NodaTime.LocalTime" => "time",
                "NodaTime.Duration" => "duration",
                "NodaTime.Period" => "duration",
                "NodaTime.Offset" => "duration",
                _ => null,
            };

            if (nodaTimeFormat is not null)
            {
                return ("string", nodaTimeFormat);
            }
        }

        // IParsable<T> types serialize as strings; try to infer their format from a single
        // wrapped field (e.g. a readonly record struct wrapping Guid → format "uuid").
        var parsableFormat = InferIParsableFormat(underlying);
        if (parsableFormat is not null)
        {
            return parsableFormat.Value;
        }

        // Check for dictionary, read-only ones included (renders as object with additionalProperties).
        if (OpenApiDictionaryValues.GetValueType(underlying) is not null)
        {
            return ("object", null);
        }

        // Check for enumerable (renders as array with items), which the type may be itself (R144: GetInterfaces does not list
        // the type, so a property typed as the IEnumerable<T> interface was a string).
        if (underlying != typeof(string) && underlying.GetInterfaces().Append(underlying).Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
        {
            return ("array", null);
        }

        // A type whose type converter converts it to string, such as a strongly typed id, binds from a route or query
        // string and is documented as the string legacy documented it as.
        if (!underlying.IsEnum && OpenApiStringConvertibleTypes.IsConvertibleToString(underlying))
        {
            return ("string", null);
        }

        // Complex types (classes/structs with no known primitive mapping) render as "object".
        if (underlying.IsClass || (underlying.IsValueType && !underlying.IsPrimitive && !underlying.IsEnum))
        {
            return ("object", null);
        }

        return ("string", null);
    }

    /// <summary>
    /// Detects whether a type implements <c>IParsable&lt;T&gt;</c> and, if so, infers the OpenAPI
    /// schema type and format by examining the type's instance fields. When the type wraps a single
    /// field whose format is well-known (e.g. a <see cref="Guid"/> field → <c>"uuid"</c>), the
    /// inferred format propagates to the OpenAPI parameter schema so consumers see the correct
    /// constraint instead of a generic <c>"object"</c>.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "IParsable types are preserved by the source generator's generated pipeline code.")]
    private static (string SchemaType, string? SchemaFormat)? InferIParsableFormat(Type type)
    {
        var isParsable = type.GetInterfaces().Any(
            static i => i.IsGenericType && i.Name.StartsWith("IParsable`", StringComparison.Ordinal));

        if (!isParsable)
        {
            return null;
        }

        // Try to infer a richer format from the type's single backing field (e.g. Guid → "uuid").
        var instanceFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (instanceFields.Length == 1)
        {
            var wrappedType = instanceFields[0].FieldType;
            if (wrappedType == typeof(Guid))
            {
                return ("string", "uuid");
            }

            if (wrappedType == typeof(int))
            {
                return ("integer", "int32");
            }

            if (wrappedType == typeof(long))
            {
                return ("integer", "int64");
            }
        }

        // IParsable without a recognisable wrapped field still serialises as a string.
        return ("string", null);
    }

    /// <summary>
    /// Scans the descriptor's URL formats for catch-all route placeholders (<c>{*name}</c> or <c>{**name}</c>)
    /// and returns the set of parameter names that require <c>allowReserved: true</c> in OpenAPI.
    /// </summary>
    private static HashSet<string> BuildCatchAllParameterNames(ApiOperationDescriptor descriptor)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in descriptor.Urls)
        {
            var remaining = url.UrlFormat.AsSpan();

            while (true)
            {
                var open = remaining.IndexOf('{');
                if (open < 0)
                {
                    break;
                }

                remaining = remaining[(open + 1)..];
                var close = remaining.IndexOf('}');
                if (close < 0)
                {
                    break;
                }

                var placeholder = remaining[..close];
                remaining = remaining[(close + 1)..];

                // Catch-all: {*name} or {**name}
                if (placeholder.Length > 1 && placeholder[0] == '*')
                {
                    var paramName = placeholder[1] == '*'
                        ? placeholder[2..]
                        : placeholder[1..];

                    // Strip any inline constraint (e.g. {**path:required})
                    var colonIdx = paramName.IndexOf(':');
                    if (colonIdx >= 0)
                    {
                        paramName = paramName[..colonIdx];
                    }

                    if (paramName.Length > 0)
                    {
                        names.Add(paramName.ToString());
                    }
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Finds the schema component a request-body property of an object type references, as legacy Blueprint referenced
    /// one, and records a warning for a type the document cannot describe.
    /// </summary>
    /// <remarks>
    /// Legacy generated a schema for any type by reflection. The document describes a type from the app's serializer
    /// context instead, so it can reference a component for one the context describes. It can also reference an
    /// operation's type, which the context need not describe: legacy's schema for it was that operation's request body,
    /// the one schema it made for the type. For any other type it records a warning naming the type, which the consumer
    /// can resolve by declaring the type on the context, rather than leave the property an unexplained object.
    /// </remarks>
    private sealed class RequestBodyTypeResolver(
        OpenApiSchemaProjectionContext schemaProjection,
        FrozenDictionary<Type, string> requestBodyNamesByType,
        List<string> warnings)
    {
        private const string StringRemediation = "or give it a [TypeConverter] that converts it to string, for the document to describe it as a string.";

        /// <summary>Returns the component a property of <paramref name="type"/> references, if it has one.</summary>
        /// <param name="descriptor">The operation whose request body has the property.</param>
        /// <param name="propertyName">The property's CLR name.</param>
        /// <param name="type">The property's type.</param>
        /// <returns>The component's name, or <see langword="null"/> when the property stays an inline object.</returns>
        public string? FindSchemaName(ApiOperationDescriptor descriptor, string propertyName, Type? type)
        {
            if (type is null)
            {
                return null;
            }

            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            if (OpenApiOperationRenderingContext.FindSchemaName(underlying, schemaProjection.SchemaNamesByType) is { } schemaName)
            {
                return schemaName;
            }

            if (requestBodyNamesByType.TryGetValue(underlying, out var requestBodyName))
            {
                return requestBodyName;
            }

            this.WarnIfUndescribed(descriptor, propertyName, underlying, isQueryParameter: false);

            return null;
        }

        /// <summary>Records a warning when the serializer context does not describe <paramref name="type"/>.</summary>
        /// <param name="descriptor">The operation whose request body has the property.</param>
        /// <param name="propertyName">The property's CLR name.</param>
        /// <param name="type">The type the property is, or holds.</param>
        /// <param name="isQueryParameter">
        /// Whether the property is a query parameter, whose schema is never a component, rather than a request-body property
        /// (or its items or values).
        /// </param>
        /// <param name="collectionType">
        /// The collection or dictionary type of the property when <paramref name="type"/> is its items or values, which the
        /// document reaches the item through.
        /// </param>
        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved for operation types registered with the serializer context.")]
        public void WarnIfUndescribed(ApiOperationDescriptor descriptor, string propertyName, Type? type, bool isQueryParameter, Type? collectionType = null)
        {
            if (type is null
                || schemaProjection.Document.App.AppOwnedSerializerContext is not System.Text.Json.Serialization.JsonSerializerContext serializerContext)
            {
                return;
            }

            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            // An open value is an object with no properties by nature, and a dictionary's values are checked on their own.
            // JsonDocument, like JsonElement and a JsonNode, is STJ's own open-value type: it always has a converter, so
            // it would otherwise reach the Kind check below and be warned about as if undescribed.
            if (underlying == typeof(object)
                || underlying == typeof(System.Text.Json.JsonElement)
                || underlying == typeof(System.Text.Json.JsonDocument)
                || typeof(System.Text.Json.Nodes.JsonNode).IsAssignableFrom(underlying)
                || OpenApiDictionaryValues.GetValueType(underlying) is not null)
            {
                return;
            }

            // GetTypeInfo succeeds for any type the serializer context can serialize at all, including one it only knows
            // through a [JsonConverter] — but the schema projection can describe an object's properties only when STJ's
            // own JsonTypeInfoKind is Object; a converter-written type renders exactly as bare as an undescribed one, so
            // it must be warned about the same way.
            var typeInfo = TryGetTypeInfo(serializerContext, underlying);
            if (typeInfo?.Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
            {
                // R169: the schema projection reaches a request body's nested types through the types the context has
                // metadata for, so an item the context describes is still no component when the collection that holds it
                // is not declared (and no declared type reaches it): the property stays a bare object while the item looks
                // described. Declaring the collection type too is what makes the document reach the item.
                if (collectionType is not null && TryGetTypeInfo(serializerContext, collectionType) is null)
                {
                    var collectionName = OpenApiSchemaProjectionContext.FormatFullNameDisplayStyle(collectionType);

                    warnings.Add(
                        $"{descriptor.Name}.{propertyName}: the serializer context {serializerContext.GetType().Name} describes {underlying.FullName} but not "
                        + $"{collectionName}, the collection that holds it, so the OpenAPI document cannot reach it and shows it as an object with "
                        + $"no properties. Declare [JsonSerializable(typeof({collectionName}))] on {serializerContext.GetType().Name} too for the "
                        + "document to describe it as a component.");
                }

                return;
            }

            // R131: each remediation is the one that changes the document for its case. Declaring the type on the context
            // does only for a type the context does not describe at all, and only where the property can reference a
            // component. A query parameter never does, and a converter-written type is described already; for both, only
            // IParsable<T> or a TypeConverter to string takes MapClrTypeToOpenApiSchema off its object branch.
            var contextName = serializerContext.GetType().Name;
            var typeName = underlying.FullName;
            var prefix = $"{descriptor.Name}.{propertyName}: ";

            if (isQueryParameter)
            {
                warnings.Add(
                    prefix + $"the OpenAPI document shows the query parameter's type {typeName} as an object with no properties. A query "
                    + $"parameter is never a component, so declaring [JsonSerializable(typeof({typeName}))] on {contextName} does not "
                    + $"change that. Implement IParsable<T> on {typeName}, {StringRemediation}");
            }
            else if (typeInfo is not null)
            {
                warnings.Add(
                    prefix + $"the serializer context {contextName} describes {typeName} as JsonTypeInfoKind.{typeInfo.Kind}, written by a "
                    + "converter rather than as an object with properties, so the OpenAPI document shows it as an object with no "
                    + $"properties. Declaring it on {contextName} does not change that, as the document cannot see what a converter "
                    + $"writes. If the converter writes a string, implement IParsable<T> on {typeName}, {StringRemediation}");
            }
            else
            {
                warnings.Add(
                    prefix + $"the serializer context {contextName} does not describe {typeName}, so the OpenAPI document shows it as an "
                    + $"object with no properties. Declare [JsonSerializable(typeof({typeName}))] on {contextName} for the document to "
                    + "describe it as a component.");
            }
        }

        private static System.Text.Json.Serialization.Metadata.JsonTypeInfo? TryGetTypeInfo(
            System.Text.Json.Serialization.JsonSerializerContext serializerContext,
            Type type)
        {
            try
            {
                return serializerContext.GetTypeInfo(type);
            }
            catch (NotSupportedException)
            {
                // A type whose converter the source generator cannot configure is not described either.
                return null;
            }
        }
    }
}

/// <summary>
/// Captures one rendered OpenAPI-style operation contract while preserving stable identity, ordered route formats, and
/// the previously rendered response objects.
/// </summary>
/// <param name="OperationName">The stable descriptor name for the operation.</param>
/// <param name="OperationTypeName">The CLR type name for the operation used as stable identity across later seams.</param>
/// <param name="HttpMethods">The deterministic HTTP method inventory published for the operation.</param>
/// <param name="UrlFormats">The deterministic route templates published for the operation.</param>
/// <param name="Tags">The deterministic OpenAPI tag inventory projected from descriptor-published group metadata.</param>
/// <param name="Responses">The rendered OpenAPI-style response objects for the operation.</param>
/// <param name="Parameters">The projected OpenAPI-style parameters from the property ownership plan.</param>
/// <param name="RequestBody">The projected OpenAPI-style request body from body-owned properties, or null when the operation has no body.</param>
public sealed record OpenApiOperationModel(
    string OperationName,
    string OperationTypeName,
    ImmutableArray<string> HttpMethods,
    ImmutableArray<string> UrlFormats,
    ImmutableArray<string> Tags,
    ImmutableArray<OpenApiResponseObjectModel> Responses,
    ImmutableArray<OpenApiParameterModel> Parameters,
    OpenApiRequestBodyModel? RequestBody)
{
    /// <summary>
    /// Gets the name of the empty schema component an operation with no request body has, before the schema name
    /// formatter is applied, or <see langword="null"/> for none: under <see cref="OpenApiSchemaNaming.Legacy"/>, a non-GET
    /// operation whose body has no properties, or one whose body an operation processor dropped, as legacy registered its
    /// type's schema before it dropped the body.
    /// </summary>
    public string? BodylessRequestSchemaName { get; init; }

    /// <summary>Gets the description of the <see cref="BodylessRequestSchemaName"/> component, or null for none.</summary>
    public string? BodylessRequestSchemaDescription { get; init; }

    /// <summary>Gets the short summary for this operation in the OpenAPI output, or null when not specified.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets the longer description for this operation in the OpenAPI output, or null when not specified.</summary>
    public string? Description { get; init; }

    /// <summary>Gets whether this operation is deprecated in the OpenAPI output (maps from <see cref="ObsoleteAttribute"/>).</summary>
    public bool Deprecated { get; init; }

    /// <summary>Gets the deprecation reason extracted from the <see cref="ObsoleteAttribute.Message"/>, or null when unset.</summary>
    public string? DeprecationReason { get; init; }

    /// <summary>Gets the tag descriptions for this operation's tags, keyed by tag name.</summary>
    public ImmutableDictionary<string, string> TagDescriptions { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>Gets whether this operation requires authentication (maps from <see cref="AuthenticationRequirement.Authenticated"/>).</summary>
    public bool RequiresAuthentication { get; init; }
}
