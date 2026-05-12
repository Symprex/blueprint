using System.Collections.Immutable;
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
        var schemaComponents = pathItemRendering.OperationRendering.ResponseObjects.SchemaComponents.Components
            .OrderBy(static component => component.BodyTypeName, StringComparer.Ordinal)
            .ToImmutableArray();

        var app = pathItemRendering.OperationRendering.ResponseObjects.SchemaComponents.SchemaProjection.Document.App;
        schemaComponents = EnrichWithResourceLinks(schemaComponents, app);

        return new OpenApiDocumentRenderingContext(pathItemRendering, paths, schemaComponents);
    }

    /// <summary>
    /// Walks the app's registered links and attaches <c>x-links</c> extension data to schema components
    /// whose body type implements <see cref="ILinkableResource"/>. Only non-self links are included because
    /// "self" links are implicit in the resource's own endpoint.
    /// </summary>
    private static ImmutableArray<OpenApiSchemaComponentModel> EnrichWithResourceLinks(
        ImmutableArray<OpenApiSchemaComponentModel> components,
        BlueprintApp app)
    {
        // Build a lookup of resource FullName → non-self links, matching the BodyTypeName format
        // used by the schema projection (Type.FullName for non-string types).
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

            var resourceName = link.ResourceType.FullName ?? link.ResourceType.Name;

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
}
