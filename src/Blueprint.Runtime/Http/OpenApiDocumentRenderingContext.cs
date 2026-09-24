using System.Collections.Immutable;
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
    /// Creates a deterministic OpenAPI-style document model from the supplied path-item rendering context.
    /// This seam intentionally stays model-only: it assembles the current path and schema inventories without widening
    /// into JSON serialization, endpoint exposure, titles, tags, or customization hooks.
    /// </summary>
    /// <param name="pathItemRendering">The path-item rendering context to assemble into a document model.</param>
    /// <returns>The assembled OpenAPI-style document model.</returns>
    public static OpenApiDocumentRenderingContext Create(OpenApiPathItemRenderingContext pathItemRendering)
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
        schemaComponents = EnrichWithResourceLinks(schemaComponents, schemaProjection.Document.App, schemaProjection.FullNameToShortName);

        // Collect distinct enum types referenced across all schema component properties and emit them
        // as top-level component schemas so properties can use $ref instead of inline enum arrays.
        var enumComponents = CollectEnumSchemaComponents(schemaComponents);

        if (!enumComponents.IsEmpty)
        {
            schemaComponents = schemaComponents.AddRange(enumComponents)
                .OrderBy(static component => component.BodyTypeName, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        // Promote ResourceEvent envelopes from inline response schemas to named component schemas so
        // TypeScript generators (which resolve types from $ref paths) can produce typed envelope classes
        // like ResourceEventOfTemplateApiResource instead of anonymous inline objects.
        var (envelopeComponents, changeTypeEnum) = CollectResourceEventEnvelopeComponents(paths);

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

        foreach (var path in paths)
        {
            foreach (var method in path.Methods)
            {
                var primaryOperation = method.Operations.IsEmpty ? null : method.Operations[0];

                if (primaryOperation?.RequestBody is not { } requestBody || requestBody.Properties.IsEmpty)
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
                        EnumDescription = bodyProperty.EnumDescription,
                    });
                }

                builder.Add(new OpenApiSchemaComponentModel(
                    requestBody.SchemaName ?? primaryOperation.OperationName,
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
        IReadOnlyDictionary<string, string> fullNameToShortName)
    {
        // Build a lookup of resource name → non-self links, keyed by the same BodyTypeName schema projection gives the
        // resource's component: the short name it maps a covered type's CLR full name to. A resource with no mapping
        // keeps its full name, so a component keyed by full name still matches. Any SchemaNameFormatter is applied to
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
            // runtime never adds such a link to a resource's $links either.
            if (!link.OperationDescriptor.IsExposed)
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
    private static ImmutableArray<OpenApiSchemaComponentModel> CollectEnumSchemaComponents(
        ImmutableArray<OpenApiSchemaComponentModel> existingComponents)
    {
        var enumSchemas = new Dictionary<string, (ImmutableArray<string> Values, string? Description)>(StringComparer.Ordinal);

        foreach (var component in existingComponents)
        {
            foreach (var property in component.Properties)
            {
                if (property.EnumSchemaRef is not null && !property.EnumValues.IsDefaultOrEmpty)
                {
                    enumSchemas.TryAdd(property.EnumSchemaRef, (property.EnumValues, property.EnumDescription));
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
        CollectResourceEventEnvelopeComponents(ImmutableArray<OpenApiPathItemModel> paths)
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
            builder.Add(CreateResourceEventEnvelopeComponent(envelopeBodyTypeName, innerBodyTypeName));
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
        string envelopeBodyTypeName, string innerBodyTypeName)
    {
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
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.ChangeType))),
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
                Description: DescribeResourceEventProperty(nameof(ResourceEvent.Href))),
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

    private static string? DescribeResourceEventProperty(string propertyName)
    {
        return typeof(ResourceEvent).GetProperty(propertyName) is { } property ? OpenApiXmlDocs.GetSchemaSummary(property) : null;
    }
}
