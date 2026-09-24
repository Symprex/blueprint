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
        ArgumentNullException.ThrowIfNull(responseObjects);

        var naming = responseObjects.SchemaComponents.SchemaProjection.Naming;
        var operations = responseObjects.Operations
            .Select(operation => CreateOperation(operation, naming))
            .ToImmutableArray();

        return new OpenApiOperationRenderingContext(responseObjects, operations);
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

    private static OpenApiOperationModel CreateOperation(OpenApiResponseObjectOperationContext operation, OpenApiSchemaNaming naming)
    {
        var httpMethods = operation.Operation.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var httpMethodMetadata)
            ? httpMethodMetadata.HttpMethods
                .OrderBy(static httpMethod => httpMethod, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
        var urlFormats = operation.Operation.Urls
            .Select(static url => url.UrlFormat)
            .OrderBy(static urlFormat => urlFormat, StringComparer.Ordinal)
            .ToImmutableArray();
        var tags = operation.Operation.TryGetFeatureData<OperationGroupMetadataFeatureData>(out var operationGroupMetadata)
            && string.IsNullOrWhiteSpace(operationGroupMetadata.GroupName) == false
            ? ImmutableArray.Create(operationGroupMetadata.GroupName)
            : ImmutableArray<string>.Empty;
        var tagDescriptions = operationGroupMetadata?.GroupDescription is { Length: > 0 } desc
            ? ImmutableDictionary<string, string>.Empty.Add(operationGroupMetadata.GroupName, desc)
            : ImmutableDictionary<string, string>.Empty;

        var (parameters, requestBody) = ProjectInputMetadata(operation.Operation, naming);

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
            Summary = operationDescription,
            Description = operationRemarks,
            Deprecated = isDeprecated,
            DeprecationReason = deprecationReason,
            RequiresAuthentication = requiresAuthentication,
            TagDescriptions = tagDescriptions,
        };
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "OpenAPI projection reflects over operation type properties that are preserved by the source generator's generated pipeline code.")]
    private static (ImmutableArray<OpenApiParameterModel> Parameters, OpenApiRequestBodyModel? RequestBody)
        ProjectInputMetadata(ApiOperationDescriptor descriptor, OpenApiSchemaNaming naming)
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

        foreach (var entry in ownershipData.Entries)
        {
            var clrProperty = descriptor.OperationType.GetProperty(
                entry.PropertyName,
                BindingFlags.Public | BindingFlags.Instance);

            // Prefer CLR type-based mapping when property info is available for accurate collection/dictionary support.
            var (schemaType, schemaFormat) = clrProperty is not null
                ? MapClrTypeToOpenApiSchema(clrProperty.PropertyType)
                : MapClrTypeToOpenApiSchema(entry.PropertyTypeName);
            var constraints = OpenApiValidationConstraints.ExtractFromProperty(clrProperty);
            var description = GetPropertyDescription(clrProperty, entry.PropertyName, docData);
            var enumValues = GetEnumValues(clrProperty);
            var enumSchemaRef = GetEnumSchemaRef(clrProperty, naming);

            // Validation-derived format (email, uri, phone) takes precedence over CLR type format.
            var effectiveFormat = constraints.Format ?? schemaFormat;
            var obsoletePropertyAttr = clrProperty?.GetCustomAttribute<System.ObsoleteAttribute>();
            var isDeprecated = obsoletePropertyAttr is not null;
            var propertyDeprecationReason = obsoletePropertyAttr?.Message;
            var defaultValueAttr = clrProperty?.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>();
            var exampleValueAttr = clrProperty?.GetCustomAttribute<Blueprint.Model.Attributes.OpenApiExampleAttribute>();
            var isWriteOnly = clrProperty?.GetCustomAttribute<Blueprint.Model.Attributes.OpenApiWriteOnlyAttribute>() is not null;

            // Resolve array element schema for collection-typed query/header/cookie parameters
            string? paramItemsType = null;
            string? paramItemsFormat = null;
            if (schemaType == "array" && clrProperty is not null)
            {
                var (itemsType, itemsFormat, _, _, _, _) = ResolveCollectionSchema(clrProperty);
                paramItemsType = itemsType;
                paramItemsFormat = itemsFormat;
            }

            switch (entry.Source)
            {
                case PropertyBindingSource.Route:
                    var bindingName = entry.BindingName ?? entry.PropertyName;
                    parameters.Add(new OpenApiParameterModel(
                        bindingName,
                        "path",
                        Required: true,
                        schemaType,
                        effectiveFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                        AllowReserved = catchAllNames.Contains(bindingName),
                    });
                    break;

                case PropertyBindingSource.Query:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "query",
                        Required: constraints.IsRequired,
                        schemaType,
                        effectiveFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                        ItemsType = paramItemsType,
                        ItemsFormat = paramItemsFormat,
                    });
                    break;

                case PropertyBindingSource.Header:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "header",
                        Required: constraints.IsRequired,
                        schemaType,
                        effectiveFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                    });
                    break;

                case PropertyBindingSource.Cookie:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "cookie",
                        Required: constraints.IsRequired,
                        schemaType,
                        effectiveFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        ExampleValue = exampleValueAttr?.Value,
                    });
                    break;

                case PropertyBindingSource.Body:
                    var (bodyItemsType, bodyItemsFormat, bodyItemsSchemaRef, bodyAdditionalType, bodyAdditionalFormat, bodyAdditionalSchemaRef) = ResolveCollectionSchema(clrProperty);

                    bodyPropertyNamesByClrName[entry.PropertyName] = entry.BindingName ?? entry.PropertyName;
                    conditionalRequirements.AddRange(OpenApiValidationConstraints.ExtractConditionalRequirements(clrProperty));

                    bodyProperties.Add(new OpenApiRequestBodyPropertyModel(
                        entry.BindingName ?? entry.PropertyName,
                        schemaType,
                        effectiveFormat)
                    {
                        Description = description,
                        IsRequired = constraints.IsRequired,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        EnumSchemaRef = enumSchemaRef,
                        IsNullable = entry.IsNullable,
                        ItemsType = bodyItemsType,
                        ItemsFormat = bodyItemsFormat,
                        ItemsSchemaRef = bodyItemsSchemaRef,
                        AdditionalPropertiesType = bodyAdditionalType,
                        AdditionalPropertiesFormat = bodyAdditionalFormat,
                        AdditionalPropertiesSchemaRef = bodyAdditionalSchemaRef,
                        IsDeprecated = isDeprecated,
                        DeprecationReason = propertyDeprecationReason,
                        DefaultValue = defaultValueAttr?.Value,
                        ExampleValue = exampleValueAttr?.Value,
                        IsWriteOnly = isWriteOnly,
                    });
                    break;
            }
        }

        var orderedParameters = parameters
            .OrderBy(static p => p.In, StringComparer.Ordinal)
            .ThenBy(static p => p.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        var requestBody = bodyProperties.Count > 0
            ? new OpenApiRequestBodyModel(Required: true, bodyProperties.ToImmutableArray())
            {
                Conditionals = ResolveBodyConditionals(conditionalRequirements, bodyPropertyNamesByClrName),
            }
            : null;

        return (orderedParameters, requestBody);
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
        OperationDocumentationFeatureData? docData)
    {
        var attributeDescription = property?
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;

        if (attributeDescription is not null)
        {
            return attributeDescription;
        }

        // Fall back to compile-time XML documentation when no [Description] attribute is present
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
    private static string? GetEnumSchemaRef(System.Reflection.PropertyInfo? property, OpenApiSchemaNaming naming)
    {
        if (property is null)
        {
            return null;
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return propertyType.IsEnum ? OpenApiSchemaNames.Format(propertyType, naming) : null;
    }

    /// <summary>
    /// Resolves collection/dictionary element schema information from a CLR property for OpenAPI rendering.
    /// Returns typed items info for arrays/lists and additionalProperties info for dictionaries.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved for operation types registered with the serializer context.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Type interfaces are preserved for operation types registered with the serializer context.")]
    private static (string? ItemsType, string? ItemsFormat, string? ItemsSchemaRef, string? AdditionalPropertiesType, string? AdditionalPropertiesFormat, string? AdditionalPropertiesSchemaRef) ResolveCollectionSchema(
        System.Reflection.PropertyInfo? property)
    {
        if (property is null)
        {
            return (null, null, null, null, null, null);
        }

        var propertyType = property.PropertyType;

        // string implements IEnumerable<char> — skip it to avoid emitting a spurious items schema.
        if (propertyType == typeof(string))
        {
            return (null, null, null, null, null, null);
        }

        // Check for IDictionary<TKey, TValue> first (dictionaries also implement IEnumerable).
        var dictInterface = propertyType.GetInterfaces()
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictInterface is not null)
        {
            var valueType = dictInterface.GetGenericArguments()[1];
            var (additionalType, additionalFormat) = MapClrTypeToOpenApiSchema(valueType);

            // When the value type maps to "object", it's a complex type. We render as
            // type: "object" inline rather than $ref because the component schemas section
            // is populated from response projections and the type may not appear there.
            return (null, null, null, additionalType, additionalFormat, null);
        }

        // Check for IEnumerable<T>.
        var enumInterface = propertyType.GetInterfaces()
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumInterface is not null)
        {
            var elementType = enumInterface.GetGenericArguments()[0];
            var (itemType, itemFormat) = MapClrTypeToOpenApiSchema(elementType);

            // When the element type maps to "object", it's a complex type. We render as
            // type: "object" inline rather than $ref because the component schemas section
            // is populated from response projections and the type may not appear there.
            return (itemType, itemFormat, null, null, null, null);
        }

        return (null, null, null, null, null, null);
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

        // IParsable<T> types serialize as strings; try to infer their format from a single
        // wrapped field (e.g. a readonly record struct wrapping Guid → format "uuid").
        var parsableFormat = InferIParsableFormat(underlying);
        if (parsableFormat is not null)
        {
            return parsableFormat.Value;
        }

        // Check for dictionary (renders as object with additionalProperties).
        if (underlying.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
        {
            return ("object", null);
        }

        // Check for enumerable (renders as array with items).
        if (underlying != typeof(string) && underlying.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
        {
            return ("array", null);
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
