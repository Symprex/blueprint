using System.Collections.Immutable;

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

        return new OpenApiDocumentRenderingContext(pathItemRendering, paths, schemaComponents);
    }
}
