using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Blueprint.Http;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Assembles the current deterministic OpenAPI path-item and schema-component seams into one host-facing document model
/// so later output and endpoint slices can consume a single stable runtime contract without rewalking earlier stages.
/// </summary>
/// <param name="PathItemRendering">The previously rendered path-item context that owns the current route inventory.</param>
/// <param name="Paths">The deterministic path-item inventory for the assembled document model.</param>
/// <param name="SchemaComponents">The deterministic reusable schema-component inventory for the assembled document model.</param>
public sealed record OpenApiDocumentRenderingContext(
    OpenApiPathItemRenderingContext PathItemRendering,
    ImmutableArray<OpenApiPathItemModel> Paths,
    ImmutableArray<OpenApiSchemaComponentModel> SchemaComponents)
{
    /// <summary>
    /// Creates a deterministic OpenAPI-style document model from the supplied path-item rendering context, with the
    /// framework's own ResourceEvent envelope descriptions.
    /// </summary>
    /// <remarks>
    /// The 1.0.2 signature, kept so that a binary built against it still binds: <c>useLegacyResourceEventText</c> was added
    /// in 1.0.3 as an optional parameter, which a recompile fills in but an existing binary cannot.
    /// </remarks>
    /// <param name="pathItemRendering">The path-item rendering context to assemble into a document model.</param>
    /// <returns>The assembled OpenAPI-style document model.</returns>
    public static OpenApiDocumentRenderingContext Create(OpenApiPathItemRenderingContext pathItemRendering)
    {
        return Create(pathItemRendering, useLegacyResourceEventText: false);
    }

    /// <summary>
    /// Creates a deterministic OpenAPI-style document model from the supplied path-item rendering context.
    /// This seam intentionally stays model-only: it assembles the current path and schema inventories without widening
    /// into JSON serialization, endpoint exposure, titles, tags, or customization hooks.
    /// </summary>
    /// <param name="pathItemRendering">The path-item rendering context to assemble into a document model.</param>
    /// <param name="useLegacyResourceEventText">
    /// When <see langword="true"/>, the <c>changeType</c> and <c>href</c> properties of a synthesized ResourceEvent
    /// envelope carry Signature365's legacy Blueprint's own worded descriptions (typo included) rather than the
    /// framework's neutral ones — set from <c>infoOptions?.SchemaNaming == OpenApiSchemaNaming.Legacy</c> by the
    /// caller, the same way every other Legacy-only rendering choice is gated.
    /// </param>
    /// <returns>The assembled OpenAPI-style document model.</returns>
    public static OpenApiDocumentRenderingContext Create(OpenApiPathItemRenderingContext pathItemRendering, bool useLegacyResourceEventText = false)
    {
        ArgumentNullException.ThrowIfNull(pathItemRendering);

        var paths = pathItemRendering.PathItems
            .OrderBy(static pathItem => pathItem.UrlFormat, StringComparer.Ordinal)
            .ToImmutableArray();
        var responseSchemaComponents = pathItemRendering.OperationRendering.ResponseObjects.SchemaComponents.Components
            .OrderBy(static component => component.BodyTypeName, StringComparer.Ordinal)
            .ToImmutableArray();

        var requestBodySchemaComponents = CollectRequestBodySchemaComponents(paths);
        var schemaComponents = responseSchemaComponents.AddRange(requestBodySchemaComponents)
            .OrderBy(static component => component.BodyTypeName, StringComparer.Ordinal)
            .ToImmutableArray();

        var schemaProjection = pathItemRendering.OperationRendering.ResponseObjects.SchemaComponents.SchemaProjection;
        schemaComponents = EnrichWithResourceLinks(schemaComponents, schemaProjection.Document.App, schemaProjection.ProjectedFullNameToName, keepLinksToUnexposedOperations: schemaProjection.Naming == OpenApiSchemaNaming.Legacy);

        // Collect distinct enum types referenced across all schema component properties and emit them
        // as top-level component schemas so properties can use $ref instead of inline enum arrays.
        var enumComponents = CollectEnumSchemaComponents(schemaComponents, paths, schemaProjection.SchemaNamesByType);

        if (!enumComponents.IsEmpty)
        {
            schemaComponents = schemaComponents.AddRange(enumComponents)
                .OrderBy(static component => component.BodyTypeName, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        // Promote ResourceEvent envelopes from inline response schemas to named component schemas so
        // TypeScript generators (which resolve types from $ref paths) can produce typed envelope classes
        // like ResourceEventOfTemplateApiResource instead of anonymous inline objects.
        var (envelopeComponents, changeTypeEnum) = CollectResourceEventEnvelopeComponents(paths, useLegacyResourceEventText);

        if (!envelopeComponents.IsEmpty)
        {
            schemaComponents = schemaComponents.AddRange(envelopeComponents);

            if (changeTypeEnum is not null
                && !schemaComponents.Any(c => string.Equals(c.BodyTypeName, changeTypeEnum.BodyTypeName, StringComparison.Ordinal)))
            {
                schemaComponents = schemaComponents.Add(changeTypeEnum);
            }

            schemaComponents = schemaComponents
                .OrderBy(static component => component.BodyTypeName, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        return new OpenApiDocumentRenderingContext(pathItemRendering, paths, schemaComponents);
    }

    /// <summary>
    /// Walks the assembled path items to find operations with request bodies and converts each into a reusable
    /// <see cref="OpenApiSchemaComponentModel"/> so the request body can be referenced as
    /// <c>$ref: #/components/schemas/{OperationName}</c> instead of being rendered inline.
    /// Only the primary operation per method slot is considered (matching the operation that the output renderer uses).
    /// </summary>
    private static ImmutableArray<OpenApiSchemaComponentModel> CollectRequestBodySchemaComponents(
        ImmutableArray<OpenApiPathItemModel> paths)
    {
        var builder = ImmutableArray.CreateBuilder<OpenApiSchemaComponentModel>();

        // An operation published at several URLs has one request body, which legacy wrote once, as the operation type's
        // schema, and referenced from every URL (Signature365 8b40ecc961, Blueprint.OpenApi/OpenApiDocumentBuilder.cs:160-178).
        // Only another operation's body under the same name is a clash, left for the component writer to report.
        var operationsByBodyName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            foreach (var method in path.Methods)
            {
                var primaryOperation = method.Operations.IsEmpty ? null : method.Operations[0];

                // R178: an operation with no body has the empty schema legacy registered for its type.
                if (primaryOperation is { RequestBody: null, BodylessRequestSchemaName: { } bodylessName })
                {
                    if (operationsByBodyName.TryAdd(bodylessName, primaryOperation.OperationTypeName)
                        || !string.Equals(operationsByBodyName[bodylessName], primaryOperation.OperationTypeName, StringComparison.Ordinal))
                    {
                        builder.Add(new OpenApiSchemaComponentModel(
                            bodylessName,
                            "object",
                            ImmutableArray<string>.Empty,
                            ImmutableArray<OpenApiSchemaComponentPropertyModel>.Empty,
                            Description: primaryOperation.BodylessRequestSchemaDescription)
                        {
                            IsBodylessOperation = true,
                        });
                    }

                    continue;
                }

                if (primaryOperation?.RequestBody is not { } requestBody || requestBody.Properties.IsEmpty)
                {
                    continue;
                }

                var bodyName = requestBody.SchemaName ?? primaryOperation.OperationName;

                if (!operationsByBodyName.TryAdd(bodyName, primaryOperation.OperationTypeName)
                    && string.Equals(operationsByBodyName[bodyName], primaryOperation.OperationTypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                var requiredNames = ImmutableArray.CreateBuilder<string>();
                var properties = ImmutableArray.CreateBuilder<OpenApiSchemaComponentPropertyModel>();

                foreach (var bodyProperty in requestBody.Properties)
                {
                    if (bodyProperty.IsRequired)
                    {
                        requiredNames.Add(bodyProperty.Name);
                    }

                    properties.Add(new OpenApiSchemaComponentPropertyModel(
                        bodyProperty.Name,
                        bodyProperty.SchemaType,
                        IsExtensionData: false,
                        bodyProperty.IsNullable,
                        bodyProperty.SchemaFormat,
                        bodyProperty.EnumValues,
                        bodyProperty.EnumSchemaRef,
                        bodyProperty.Description,
                        bodyProperty.MinLength,
                        bodyProperty.MaxLength,
                        bodyProperty.Pattern,
                        bodyProperty.Minimum,
                        bodyProperty.Maximum,
                        bodyProperty.ExclusiveMinimum,
                        bodyProperty.ExclusiveMaximum,
                        bodyProperty.ItemsType,
                        bodyProperty.ItemsFormat,
                        bodyProperty.AdditionalPropertiesType,
                        bodyProperty.AdditionalPropertiesFormat,
                        bodyProperty.ItemsSchemaRef,
                        bodyProperty.AdditionalPropertiesSchemaRef,
                        AdditionalPropertiesItemsType: null,
                        AdditionalPropertiesItemsFormat: null,
                        AdditionalPropertiesItemsSchemaRef: null,
                        bodyProperty.IsDeprecated,
                        bodyProperty.DeprecationReason,
                        bodyProperty.DefaultValue,
                        bodyProperty.ExampleValue,
                        IsReadOnly: false,
                        bodyProperty.IsWriteOnly,
                        SchemaRef: bodyProperty.SchemaRef,
                        AllowsAdditionalProperties: bodyProperty.AllowsAdditionalProperties)
                    {
                        AdditionalPropertiesAreNullable = bodyProperty.AdditionalPropertiesAreNullable,
                        ItemsAreNullable = bodyProperty.ItemsAreNullable,
                        EnumDescription = bodyProperty.EnumDescription,
                    });
                }

                builder.Add(new OpenApiSchemaComponentModel(
                    bodyName,
                    "object",
                    requiredNames.ToImmutable(),
                    properties.ToImmutable(),
                    Description: requestBody.Description,
                    Conditionals: requestBody.Conditionals));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Walks the app's registered links and attaches <c>x-links</c> extension data to schema components
    /// whose body type implements <see cref="ILinkableResource"/>. Only non-self links are included because
    /// "self" links are implicit in the resource's own endpoint.
    /// </summary>
    private static ImmutableArray<OpenApiSchemaComponentModel> EnrichWithResourceLinks(
        ImmutableArray<OpenApiSchemaComponentModel> components,
        BlueprintApp app,
        IReadOnlyDictionary<string, string> fullNameToShortName,
        bool keepLinksToUnexposedOperations)
    {
        // Build a lookup of resource name → non-self links, keyed by the same BodyTypeName schema projection gives the
        // resource's component: the name it maps a projected type's CLR full name to, a type reached only through
        // another's property, list or dictionary included. A resource with no mapping keeps its full name, so a
        // component keyed by full name still matches. Any SchemaNameFormatter is applied to
        // that BodyTypeName later, when the component is written, so the links follow the component to its final key.
        var linksByResourceName = new Dictionary<string, List<OpenApiSchemaLinkExtension>>(StringComparer.Ordinal);

        foreach (var link in app.Links)
        {
            if (link.ResourceType is null)
            {
                continue;
            }

            if (string.Equals(link.Rel, "self", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The document leaves an unexposed operation out, so a link naming its operationId would dangle — and the
            // runtime never adds such a link to a resource's $links either. Legacy's BlueprintLinkSchemaProcessor filtered
            // nothing, and the generated client builds a stub for an operationId with no path.
            if (!link.OperationDescriptor.IsExposed && !keepLinksToUnexposedOperations)
            {
                continue;
            }

            var resourceFullName = link.ResourceType.FullName ?? link.ResourceType.Name;
            var resourceName = fullNameToShortName.GetValueOrDefault(resourceFullName, resourceFullName);

            if (!linksByResourceName.TryGetValue(resourceName, out var linkList))
            {
                linkList = [];
                linksByResourceName[resourceName] = linkList;
            }

            var httpMethod = link.OperationDescriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methodData)
                && !methodData.HttpMethods.IsEmpty
                ? methodData.HttpMethods[0]
                : "GET";

            linkList.Add(new OpenApiSchemaLinkExtension(link.Rel, link.OperationDescriptor.Name, httpMethod));
        }

        if (linksByResourceName.Count == 0)
        {
            return components;
        }

        var builder = ImmutableArray.CreateBuilder<OpenApiSchemaComponentModel>(components.Length);

        foreach (var component in components)
        {
            if (linksByResourceName.TryGetValue(component.BodyTypeName, out var resourceLinks))
            {
                var sortedLinks = resourceLinks
                    .OrderBy(static l => l.Rel, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray();

                builder.Add(component with { Links = sortedLinks });
            }
            else
            {
                builder.Add(component);
            }
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Scans all schema component properties for enum references (<see cref="OpenApiSchemaComponentPropertyModel.EnumSchemaRef"/>)
    /// and creates a distinct set of enum component schemas. Each enum component is rendered as
    /// <c>type: string</c> with <c>enum</c>, <c>x-enumNames</c>, and <c>x-enum-varnames</c> arrays, matching the
    /// standard pattern expected by TypeScript generators and other OpenAPI tooling.
    /// </summary>
    /// <remarks>
    /// An enum reached only as the items of an array, or the values of a dictionary, has no property of its own to carry
    /// its members, yet the property writes a <c>$ref</c> to it (R143). Each such reference to a name that is one of
    /// <paramref name="schemaNamesByType"/>'s enums gets its component too, or the reference dangles. So does an enum
    /// an array parameter's items reference (R148, under Legacy naming only).
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Enum types used in STJ-serializable operations are preserved by the generated serializer context.")]
    private static ImmutableArray<OpenApiSchemaComponentModel> CollectEnumSchemaComponents(
        ImmutableArray<OpenApiSchemaComponentModel> existingComponents,
        ImmutableArray<OpenApiPathItemModel> paths,
        FrozenDictionary<Type, string> schemaNamesByType)
    {
        var enumSchemas = new Dictionary<string, (ImmutableArray<string> Values, string? Description)>(StringComparer.Ordinal);
        var enumTypesByName = schemaNamesByType
            .Where(static pair => pair.Key.IsEnum)
            .ToDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);

        foreach (var component in existingComponents)
        {
            foreach (var property in component.Properties)
            {
                if (property.EnumSchemaRef is not null && !property.EnumValues.IsDefaultOrEmpty)
                {
                    enumSchemas.TryAdd(property.EnumSchemaRef, (property.EnumValues, property.EnumDescription));
                }

                foreach (var reference in new[] { property.ItemsSchemaRef, property.AdditionalPropertiesSchemaRef, property.AdditionalPropertiesItemsSchemaRef })
                {
                    if (reference is not null && enumTypesByName.TryGetValue(reference, out var enumType))
                    {
                        enumSchemas.TryAdd(reference, (Enum.GetNames(enumType).ToImmutableArray(), OpenApiSchemaProjectionContext.GetTypeDescription(enumType)));
                    }
                }
            }
        }

        // Only the operation written for a method slot has its parameters written, as OpenApiOutputRenderingContext does.
        foreach (var path in paths)
        {
            foreach (var method in path.Methods)
            {
                if (method.Operations.IsEmpty)
                {
                    continue;
                }

                foreach (var parameter in method.Operations[0].Parameters)
                {
                    if (parameter.ItemsEnumSchemaRef is not null && !parameter.ItemsEnumValues.IsDefaultOrEmpty)
                    {
                        enumSchemas.TryAdd(parameter.ItemsEnumSchemaRef, (parameter.ItemsEnumValues, parameter.ItemsEnumDescription));
                    }
                }
            }
        }

        if (enumSchemas.Count == 0)
        {
            return ImmutableArray<OpenApiSchemaComponentModel>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<OpenApiSchemaComponentModel>(enumSchemas.Count);

        foreach (var (name, (values, description)) in enumSchemas)
        {
            builder.Add(new OpenApiSchemaComponentModel(
                name,
                "string",
                ImmutableArray<string>.Empty,
                ImmutableArray<OpenApiSchemaComponentPropertyModel>.Empty,
                Description: description,
                ComponentEnumValues: values));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Builds the synthetic CLR-style body type name for a ResourceEvent envelope wrapping the given inner type.
    /// Both document assembly and response rendering must use this method to ensure the same schema key is produced
    /// after <see cref="OpenApiOutputRenderingContext"/> applies <c>SanitiseSchemaKey</c>.
    /// </summary>
    /// <param name="innerBodyTypeName">The full CLR body type name of the inner resource type.</param>
    /// <returns>A synthetic generic CLR name that <c>SimplifyClrTypeName</c> converts to the "Of" convention.</returns>
    /// <remarks>
    /// The synthetic name <c>Blueprint.Http.ResourceEvent&lt;Inner.Full.Name&gt;</c> is processed by
    /// <c>SimplifyClrTypeName</c> into the "Of" convention (e.g. <c>ResourceEventOfTemplateApiResource</c>)
    /// matching the NJsonSchema naming used by S365's legacy Blueprint OpenAPI output.
    /// </remarks>
    internal static string BuildResourceEventEnvelopeBodyTypeName(string innerBodyTypeName)
    {
        return $"Blueprint.Http.ResourceEvent<{innerBodyTypeName}>";
    }

    /// <summary>
    /// Walks the assembled path items to find responses marked as ResourceEvent envelopes and promotes each into
    /// a named component schema (e.g. <c>ResourceEventOfTemplateApiResource</c>) so the response can reference
    /// it via <c>$ref</c> instead of inlining the envelope shape. Also creates the <c>ResourceEventChangeType</c>
    /// enum component that the envelope's <c>changeType</c> property references via <c>$ref</c>.
    /// </summary>
    private static (ImmutableArray<OpenApiSchemaComponentModel> Envelopes, OpenApiSchemaComponentModel? ChangeTypeEnum)
        CollectResourceEventEnvelopeComponents(ImmutableArray<OpenApiPathItemModel> paths, bool useLegacyResourceEventText)
    {
        // envelopeBodyTypeName → innerBodyTypeName (deduplicated by envelope key)
        var envelopes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            foreach (var method in path.Methods)
            {
                foreach (var operation in method.Operations)
                {
                    foreach (var response in operation.Responses)
                    {
                        foreach (var content in response.Content)
                        {
                            if (!content.IsResourceEventEnvelope || content.SchemaComponents.IsEmpty)
                            {
                                continue;
                            }

                            // Single inner type: ResourceEventOf{InnerType}
                            var innerBodyTypeName = content.SchemaComponents[0].BodyTypeName;
                            var envelopeBodyTypeName = BuildResourceEventEnvelopeBodyTypeName(innerBodyTypeName);
                            envelopes.TryAdd(envelopeBodyTypeName, innerBodyTypeName);
                        }
                    }
                }
            }
        }

        if (envelopes.Count == 0)
        {
            return (ImmutableArray<OpenApiSchemaComponentModel>.Empty, null);
        }

        var builder = ImmutableArray.CreateBuilder<OpenApiSchemaComponentModel>(envelopes.Count);

        foreach (var (envelopeBodyTypeName, innerBodyTypeName) in envelopes)
        {
            builder.Add(CreateResourceEventEnvelopeComponent(envelopeBodyTypeName, innerBodyTypeName, useLegacyResourceEventText));
        }

        var changeTypeEnum = new OpenApiSchemaComponentModel(
            "ResourceEventChangeType",
            "string",
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiSchemaComponentPropertyModel>.Empty,
            ComponentEnumValues: ImmutableArray.Create("Created", "Updated", "Deleted"));

        return (builder.MoveToImmutable(), changeTypeEnum);
    }

    /// <summary>
    /// Creates a fully materialized <see cref="OpenApiSchemaComponentModel"/> for a ResourceEvent envelope.
    /// The component contains real properties so schema processors can inspect and modify them.
    /// </summary>
    private static OpenApiSchemaComponentModel CreateResourceEventEnvelopeComponent(
        string envelopeBodyTypeName, string innerBodyTypeName, bool useLegacyResourceEventText)
    {
        var changeTypeDescription = useLegacyResourceEventText
            ? LegacyChangeTypeDescription
            : DescribeResourceEventProperty(nameof(ResourceEvent.ChangeType));
        var hrefDescription = useLegacyResourceEventText
            ? LegacyHrefDescription
            : DescribeResourceEventProperty(nameof(ResourceEvent.Href));

        var properties = ImmutableArray.Create(
            new OpenApiSchemaComponentPropertyModel(
                "$object",
                "string",
                IsExtensionData: false,
                IsNullable: false,
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.Object))),
            new OpenApiSchemaComponentPropertyModel(
                "eventId",
                "string",
                IsExtensionData: false,
                IsNullable: false,
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.EventId))),
            new OpenApiSchemaComponentPropertyModel(
                "changeType",
                null,
                IsExtensionData: false,
                IsNullable: false,
                EnumSchemaRef: "ResourceEventChangeType",
                EnumValues: ImmutableArray.Create("Created", "Updated", "Deleted"),
                Description: changeTypeDescription),
            new OpenApiSchemaComponentPropertyModel(
                "created",
                "string",
                IsExtensionData: false,
                IsNullable: false,
                Format: "date-time",
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.Created))),
            new OpenApiSchemaComponentPropertyModel(
                "resourceObject",
                "string",
                IsExtensionData: false,
                IsNullable: false,
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.ResourceObject))),
            new OpenApiSchemaComponentPropertyModel(
                "href",
                "string",
                IsExtensionData: false,
                IsNullable: true,
                Description: hrefDescription),
            new OpenApiSchemaComponentPropertyModel(
                "data",
                null,
                IsExtensionData: false,
                IsNullable: false,
                SchemaRef: innerBodyTypeName),
            new OpenApiSchemaComponentPropertyModel(
                "metadata",
                "object",
                IsExtensionData: false,
                IsNullable: true,
                AllowsAdditionalProperties: true,
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.Metadata))),
            new OpenApiSchemaComponentPropertyModel(
                "secureData",
                "object",
                IsExtensionData: false,
                IsNullable: true,
                AllowsAdditionalProperties: true,
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.SecureData))),
            new OpenApiSchemaComponentPropertyModel(
                "changedValues",
                "object",
                IsExtensionData: false,
                IsNullable: true,
                AllowsAdditionalProperties: true,
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.ChangedValues))),
            new OpenApiSchemaComponentPropertyModel(
                "correlationId",
                "string",
                IsExtensionData: false,
                IsNullable: true,
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.CorrelationId))));

        // R165: legacy listed ResourceEvent's properties as declared, and the derived event's Data, which hides
        // ResourceEvent's own, last.
        if (useLegacyResourceEventText)
        {
            string[] legacyOrder = ["$object", "eventId", "changeType", "created", "resourceObject", "href", "changedValues", "metadata", "secureData", "correlationId", "data"];

            properties = [.. properties.OrderBy(property => Array.IndexOf(legacyOrder, property.Name))];
        }

        var required = ImmutableArray.Create("$object", "eventId", "changeType", "created", "resourceObject", "data");

        // Legacy described the envelope by ResourceEvent<T>'s documentation and its members by ResourceEvent's; the
        // payload's own description is the resource's.
        return new OpenApiSchemaComponentModel(
            envelopeBodyTypeName,
            "object",
            required,
            properties,
            Description: OpenApiXmlDocs.GetSchemaSummary(typeof(ResourceEvent<>)));
    }

    // Signature365's committed documents describe these two members in legacy's words: legacy's change-type summary
    // had a stray "a" and named a Type property ResourceEvent does not have, and its href summary named the
    // ResourceEventMiddleware that populated it. Under Legacy naming only, the document keeps the words, typo and
    // all, for every consumer; a Default consumer gets the framework's own neutral summary instead (below), the same
    // way every other Legacy-only text choice is opt-in rather than universal.
    private const string LegacyChangeTypeDescription =
        "Gets a the change type of this event, which is a higher-level version of the Type property,\n"
        + "indicating whether the resource was updated, created or deleted.";

    private const string LegacyHrefDescription =
        "Gets or sets the href of the resource this event represents, to be populated by the ResourceEventMiddleware\n"
        + "component from the resource type and id definition object.";

    private static string? DescribeResourceEventProperty(string propertyName)
    {
        return typeof(ResourceEvent).GetProperty(propertyName) is { } property ? OpenApiXmlDocs.GetSchemaSummary(property) : null;
    }
}
