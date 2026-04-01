using System.Collections.Immutable;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Renders stable OpenAPI-style response objects by binding the already-projected HTTP response metadata onto reusable
/// schema components. This keeps response shaping isolated from operation/document assembly while reconnecting the
/// response-metadata seam to the rendered component inventory.
/// </summary>
/// <param name="ResponseSchemaProjection">The per-response schema-reference projection that owns response ordering.</param>
/// <param name="SchemaComponents">The reusable schema components available to response-object content.</param>
/// <param name="Operations">The per-operation rendered response objects.</param>
public sealed record OpenApiResponseObjectRenderingContext(
    OpenApiResponseSchemaReferenceProjectionContext ResponseSchemaProjection,
    OpenApiSchemaComponentRenderingContext SchemaComponents,
    ImmutableArray<OpenApiResponseObjectOperationContext> Operations)
{
    private const string JsonMediaType = "application/json";

    /// <summary>
    /// Creates deterministic OpenAPI-style response objects from the supplied response-schema reference projection.
    /// This seam intentionally stays response-only: it preserves the current response metadata and only adds JSON content
    /// bindings for covered schema components, leaving operation/document assembly to later slices.
    /// </summary>
    /// <param name="responseSchemaProjection">The response-schema reference projection to render.</param>
    /// <returns>The rendered response-object context.</returns>
    public static OpenApiResponseObjectRenderingContext Create(OpenApiResponseSchemaReferenceProjectionContext responseSchemaProjection)
    {
        ArgumentNullException.ThrowIfNull(responseSchemaProjection);

        var schemaComponents = OpenApiSchemaComponentRenderingContext.Create(responseSchemaProjection.SchemaProjection);
        var componentsByBodyTypeName = schemaComponents.Components
            .ToDictionary(static component => component.BodyTypeName, StringComparer.Ordinal);
        var operations = responseSchemaProjection.Operations
            .Select(operation => CreateOperationContext(operation, componentsByBodyTypeName))
            .ToImmutableArray();

        return new OpenApiResponseObjectRenderingContext(responseSchemaProjection, schemaComponents, operations);
    }

    private static OpenApiResponseObjectOperationContext CreateOperationContext(
        OpenApiResponseSchemaReferenceOperationContext operation,
        Dictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var responses = operation.Responses
            .Select(response => CreateResponseObject(response, componentsByBodyTypeName))
            .ToImmutableArray();

        return new OpenApiResponseObjectOperationContext(operation.Operation, responses);
    }

    private static OpenApiResponseObjectModel CreateResponseObject(
        OpenApiResponseSchemaReferenceResponseContext response,
        Dictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var content = response.Response.HasResponseBody
            ? CreateContent(response.SchemaReferences, componentsByBodyTypeName)
            : ImmutableArray<OpenApiResponseContentModel>.Empty;

        return new OpenApiResponseObjectModel(
            response.Response.StatusCode,
            response.Response.HasResponseBody,
            response.Response.ResultTypeNames,
            content);
    }

    private static ImmutableArray<OpenApiResponseContentModel> CreateContent(
        ImmutableArray<OpenApiResponseSchemaReference> schemaReferences,
        Dictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var schemaComponents = schemaReferences
            .Where(reference => componentsByBodyTypeName.ContainsKey(reference.BodyTypeName))
            .Select(reference => new OpenApiResponseContentSchemaComponent(reference.BodyTypeName, componentsByBodyTypeName[reference.BodyTypeName]))
            .ToImmutableArray();

        return schemaComponents.IsEmpty
            ? ImmutableArray<OpenApiResponseContentModel>.Empty
            : [new OpenApiResponseContentModel(JsonMediaType, schemaComponents)];
    }
}

/// <summary>
/// Captures the rendered response objects for one HTTP-exposed operation.
/// </summary>
/// <param name="Operation">The HTTP-exposed operation descriptor.</param>
/// <param name="Responses">The rendered OpenAPI-style response objects for the operation.</param>
public sealed record OpenApiResponseObjectOperationContext(
    ApiOperationDescriptor Operation,
    ImmutableArray<OpenApiResponseObjectModel> Responses);

/// <summary>
/// Captures one rendered OpenAPI-style response object while preserving the original status/body/result metadata.
/// </summary>
/// <param name="StatusCode">The HTTP status code emitted for this response family.</param>
/// <param name="HasResponseBody">Whether the response family writes a response body.</param>
/// <param name="ResultTypeNames">The semantic result type names that collapse into this response family.</param>
/// <param name="Content">The rendered response content keyed by media type when covered schema components exist.</param>
public sealed record OpenApiResponseObjectModel(
    int StatusCode,
    bool HasResponseBody,
    ImmutableArray<string> ResultTypeNames,
    ImmutableArray<OpenApiResponseContentModel> Content);

/// <summary>
/// Captures one rendered media-type entry for an OpenAPI-style response object.
/// </summary>
/// <param name="MediaType">The response media type key.</param>
/// <param name="SchemaComponents">The schema components referenced by this content entry.</param>
public sealed record OpenApiResponseContentModel(
    string MediaType,
    ImmutableArray<OpenApiResponseContentSchemaComponent> SchemaComponents);

/// <summary>
/// Links a rendered response-content entry to the reusable schema component for one covered body type.
/// </summary>
/// <param name="BodyTypeName">The stable CLR body-type name from the response metadata.</param>
/// <param name="Component">The reusable rendered schema component for the body payload.</param>
public sealed record OpenApiResponseContentSchemaComponent(
    string BodyTypeName,
    OpenApiSchemaComponentModel Component);
