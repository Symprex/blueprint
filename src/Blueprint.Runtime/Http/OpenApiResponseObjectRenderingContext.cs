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
            .Select(response => CreateResponseObject(operation.Operation, response, componentsByBodyTypeName))
            .ToImmutableArray();

        return new OpenApiResponseObjectOperationContext(operation.Operation, responses);
    }

    private static OpenApiResponseObjectModel CreateResponseObject(
        ApiOperationDescriptor operation,
        OpenApiResponseSchemaReferenceResponseContext response,
        Dictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var content = response.Response.HasResponseBody
            ? CreateContent(operation, response, componentsByBodyTypeName)
            : ImmutableArray<OpenApiResponseContentModel>.Empty;

        return new OpenApiResponseObjectModel(
            response.Response.StatusCode,
            response.Response.HasResponseBody,
            response.Response.ResultTypeNames,
            content);
    }

    private static ImmutableArray<OpenApiResponseContentModel> CreateContent(
        ApiOperationDescriptor operation,
        OpenApiResponseSchemaReferenceResponseContext response,
        Dictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var schemaComponents = response.SchemaReferences
            .Select(reference => TryCreateContentSchemaComponent(reference, componentsByBodyTypeName))
            .OfType<OpenApiResponseContentSchemaComponent>()
            .ToImmutableArray();
        var discriminator = TryCreateResourceEventDiscriminator(operation, response.Response, schemaComponents);

        return schemaComponents.IsEmpty
            ? ImmutableArray<OpenApiResponseContentModel>.Empty
            : [new OpenApiResponseContentModel(JsonMediaType, schemaComponents, discriminator)];
    }

    private static OpenApiResponseContentSchemaComponent? TryCreateContentSchemaComponent(
        OpenApiResponseSchemaReference reference,
        Dictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        if (reference.IsArrayWrapper)
        {
            // Array wrappers resolve via the element type's component.
            var elementTypeName = reference.Schema.BodyTypeName;
            return componentsByBodyTypeName.TryGetValue(elementTypeName, out var elementComponent)
                ? new OpenApiResponseContentSchemaComponent(reference.BodyTypeName, elementComponent, IsArrayWrapper: true)
                : null;
        }

        return componentsByBodyTypeName.TryGetValue(reference.BodyTypeName, out var component)
            ? new OpenApiResponseContentSchemaComponent(reference.BodyTypeName, component)
            : null;
    }

    private static OpenApiResponseDiscriminatorModel? TryCreateResourceEventDiscriminator(
        ApiOperationDescriptor operation,
        OperationHttpResponseMetadata response,
        ImmutableArray<OpenApiResponseContentSchemaComponent> schemaComponents)
    {
        if (schemaComponents.Length < 2
            || operation.TryGetFeatureData<OperationResourceEventMetadataFeatureData>(out var metadata) == false)
        {
            return null;
        }

        var schemaComponentsByBodyTypeName = schemaComponents
            .ToDictionary(static component => component.BodyTypeName, StringComparer.Ordinal);
        var distinctEventIds = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<OpenApiResponseDiscriminatorCaseModel>(response.ResultTypeNames.Length);
        foreach (var resultTypeName in response.ResultTypeNames)
        {
            var outcome = metadata.Outcomes.FirstOrDefault(candidate => string.Equals(candidate.ResultTypeName, resultTypeName, StringComparison.Ordinal));
            if (outcome is null
                || !schemaComponentsByBodyTypeName.TryGetValue(outcome.BodyTypeName, out var schemaComponent)
                || !distinctEventIds.Add(outcome.EventId))
            {
                return null;
            }

            cases.Add(new OpenApiResponseDiscriminatorCaseModel(outcome.EventId, schemaComponent));
        }

        return cases.Count != schemaComponents.Length
            ? null
            : new OpenApiResponseDiscriminatorModel("eventId", cases.ToImmutableArray());
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
/// <param name="Discriminator">Optional discriminator metadata when the response schema represents a same-status union.</param>
public sealed record OpenApiResponseContentModel(
    string MediaType,
    ImmutableArray<OpenApiResponseContentSchemaComponent> SchemaComponents,
    OpenApiResponseDiscriminatorModel? Discriminator = null);

/// <summary>
/// Captures the OpenAPI discriminator metadata for a response content schema when a same-status union can be distinguished
/// by a stable serialized property.
/// </summary>
/// <param name="PropertyName">The JSON property name used as the discriminator.</param>
/// <param name="Cases">The response-schema variants keyed by discriminator value.</param>
public sealed record OpenApiResponseDiscriminatorModel(
    string PropertyName,
    ImmutableArray<OpenApiResponseDiscriminatorCaseModel> Cases);

/// <summary>
/// Links one discriminator value to the schema component rendered for that variant's content payload.
/// </summary>
/// <param name="Value">The serialized discriminator value.</param>
/// <param name="SchemaComponent">The schema component that backs this discriminator case.</param>
public sealed record OpenApiResponseDiscriminatorCaseModel(
    string Value,
    OpenApiResponseContentSchemaComponent SchemaComponent);

/// <summary>
/// Links a rendered response-content entry to the reusable schema component for one covered body type.
/// When the body type is an array wrapper, the component references the element type and the rendering
/// layer emits <c>type: array, items: { $ref: ... }</c> instead of a direct <c>$ref</c>.
/// </summary>
/// <param name="BodyTypeName">The stable CLR body-type name from the response metadata.</param>
/// <param name="Component">The reusable rendered schema component for the body payload (element type for arrays).</param>
/// <param name="IsArrayWrapper">Whether this content entry wraps the element schema in an array.</param>
public sealed record OpenApiResponseContentSchemaComponent(
    string BodyTypeName,
    OpenApiSchemaComponentModel Component,
    bool IsArrayWrapper = false);
