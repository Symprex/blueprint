using System.Collections.Frozen;
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
    private const string ProblemJsonMediaType = "application/problem+json";
    private const string PlainTextMediaType = "text/plain";
    private const string OctetStreamMediaType = "application/octet-stream";
    private const string PlainTextResultTypeName = "Blueprint.Runtime.Exec.PlainTextResult";
    private const string FileStreamResultTypeName = "Blueprint.Runtime.Exec.FileStreamResult";
    private const string ProblemDetailsTypeName = "ProblemDetails";
    private const string ValidationProblemDetailsTypeName = "ValidationProblemDetails";

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
            .ToFrozenDictionary(static component => component.BodyTypeName, StringComparer.Ordinal);
        var operations = responseSchemaProjection.Operations
            .Select(operation => CreateOperationContext(operation, componentsByBodyTypeName))
            .ToImmutableArray();

        return new OpenApiResponseObjectRenderingContext(responseSchemaProjection, schemaComponents, operations);
    }

    private static OpenApiResponseObjectOperationContext CreateOperationContext(
        OpenApiResponseSchemaReferenceOperationContext operation,
        FrozenDictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var responses = operation.Responses
            .Select(response => CreateResponseObject(operation.Operation, response, componentsByBodyTypeName))
            .ToImmutableArray();

        return new OpenApiResponseObjectOperationContext(operation.Operation, responses);
    }

    private static OpenApiResponseObjectModel CreateResponseObject(
        ApiOperationDescriptor operation,
        OpenApiResponseSchemaReferenceResponseContext response,
        FrozenDictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var content = response.Response.HasResponseBody
            ? CreateContent(operation, response, componentsByBodyTypeName)
            : ImmutableArray<OpenApiResponseContentModel>.Empty;

        var headers = InferResponseHeaders(response.Response);

        return new OpenApiResponseObjectModel(
            response.Response.StatusCode,
            response.Response.HasResponseBody,
            response.Response.ResultTypeNames,
            content,
            headers);
    }

    private static ImmutableArray<OpenApiResponseContentModel> CreateContent(
        ApiOperationDescriptor operation,
        OpenApiResponseSchemaReferenceResponseContext response,
        FrozenDictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
    {
        var schemaComponents = response.SchemaReferences
            .Select(reference => TryCreateContentSchemaComponent(reference, componentsByBodyTypeName))
            .OfType<OpenApiResponseContentSchemaComponent>()
            .ToImmutableArray();
        var discriminator = TryCreateResourceEventDiscriminator(operation, response.Response, schemaComponents);

        return schemaComponents.IsEmpty
            ? TryCreateNonJsonContent(response.Response)
            : [new OpenApiResponseContentModel(SelectMediaType(schemaComponents), schemaComponents, discriminator)];
    }

    /// <summary>
    /// Creates content entries for non-JSON body responses (e.g. <c>text/plain</c> for PlainTextResult,
    /// <c>application/octet-stream</c> for FileStreamResult) that carry a body but have no CLR payload
    /// type to schema-project. Returns empty when no known non-JSON type is present.
    /// </summary>
    private static ImmutableArray<OpenApiResponseContentModel> TryCreateNonJsonContent(
        Model.OperationHttpResponseMetadata response)
    {
        foreach (var resultTypeName in response.ResultTypeNames)
        {
            if (string.Equals(resultTypeName, PlainTextResultTypeName, StringComparison.Ordinal))
            {
                return
                [
                    new OpenApiResponseContentModel(
                        PlainTextMediaType,
                        ImmutableArray<OpenApiResponseContentSchemaComponent>.Empty,
                        InlineSchema: new OpenApiInlineSchemaModel("string")),
                ];
            }

            if (string.Equals(resultTypeName, FileStreamResultTypeName, StringComparison.Ordinal))
            {
                return
                [
                    new OpenApiResponseContentModel(
                        OctetStreamMediaType,
                        ImmutableArray<OpenApiResponseContentSchemaComponent>.Empty,
                        InlineSchema: new OpenApiInlineSchemaModel("string", "binary")),
                ];
            }
        }

        return ImmutableArray<OpenApiResponseContentModel>.Empty;
    }

    /// <summary>
    /// Selects <c>application/problem+json</c> when every schema component in the response is a
    /// ProblemDetails or ValidationProblemDetails body (RFC 9457), otherwise <c>application/json</c>.
    /// </summary>
    private static string SelectMediaType(ImmutableArray<OpenApiResponseContentSchemaComponent> schemaComponents)
    {
        if (schemaComponents.IsEmpty)
        {
            return JsonMediaType;
        }

        foreach (var component in schemaComponents)
        {
            var bodyType = component.BodyTypeName;
            if (!string.Equals(bodyType, ProblemDetailsTypeName, StringComparison.Ordinal)
                && !string.Equals(bodyType, ValidationProblemDetailsTypeName, StringComparison.Ordinal))
            {
                return JsonMediaType;
            }
        }

        return ProblemJsonMediaType;
    }

    private static OpenApiResponseContentSchemaComponent? TryCreateContentSchemaComponent(
        OpenApiResponseSchemaReference reference,
        FrozenDictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName)
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
            .ToFrozenDictionary(static component => component.BodyTypeName, StringComparer.Ordinal);
        var distinctEventIds = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<OpenApiResponseDiscriminatorCaseModel>(response.ResultTypeNames.Length);
        foreach (var resultTypeName in response.ResultTypeNames)
        {
            var outcome = metadata.Outcomes.FirstOrDefault(candidate => string.Equals(candidate.ResultTypeName, resultTypeName, StringComparison.Ordinal));
            if (outcome is null
                || !schemaComponentsByBodyTypeName.TryGetValue(NormalizeBodyTypeName(outcome.BodyTypeName), out var schemaComponent)
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

    private const string CreatedResultTypeName = "Blueprint.Runtime.Exec.CreatedResult";
    private const string ResourceEventCreatedResultPrefix = "Blueprint.Runtime.Exec.ResourceEventResult+";
    private const string RedirectResultTypeName = "Blueprint.Runtime.Http.RedirectResult";
    private const string OkResultTypeName = "Blueprint.Runtime.Exec.OkResult";

    /// <summary>
    /// Strips the namespace prefix from a compile-time full CLR body type name so it matches the short
    /// format used by schema projection. Array suffixes are preserved.
    /// </summary>
    private static string NormalizeBodyTypeName(string bodyTypeName)
    {
        var isArray = bodyTypeName.EndsWith("[]", StringComparison.Ordinal);
        var nameToNormalize = isArray ? bodyTypeName[..^2] : bodyTypeName;

        var lastDot = nameToNormalize.LastIndexOf('.');
        if (lastDot >= 0)
        {
            nameToNormalize = nameToNormalize[(lastDot + 1)..];
        }

        return isArray ? nameToNormalize + "[]" : nameToNormalize;
    }

    /// <summary>
    /// Infers well-known HTTP response headers from the response status code and result type names.
    /// Blueprint result types carry implicit header contracts — for example, <c>CreatedResult</c> sets a
    /// <c>Location</c> header, <c>FileStreamResult</c> sets <c>Content-Disposition</c>, and <c>RedirectResult</c>
    /// always sets <c>Location</c>.
    /// </summary>
    private static ImmutableArray<OpenApiResponseHeaderModel> InferResponseHeaders(
        OperationHttpResponseMetadata response)
    {
        var headers = new List<OpenApiResponseHeaderModel>(2);

        var hasLocation = false;
        var hasContentDisposition = false;

        foreach (var resultTypeName in response.ResultTypeNames)
        {
            if (!hasLocation
                && (string.Equals(resultTypeName, CreatedResultTypeName, StringComparison.Ordinal)
                    || string.Equals(resultTypeName, RedirectResultTypeName, StringComparison.Ordinal)
                    || resultTypeName.StartsWith(ResourceEventCreatedResultPrefix, StringComparison.Ordinal)))
            {
                hasLocation = true;
            }

            if (!hasContentDisposition
                && string.Equals(resultTypeName, FileStreamResultTypeName, StringComparison.Ordinal))
            {
                hasContentDisposition = true;
            }
        }

        // 201 Created always implies a Location header even if we didn't see CreatedResult
        // (the executor sets Location from self-link on any 201).
        if (!hasLocation && response.StatusCode == 201)
        {
            hasLocation = true;
        }

        // Redirect status codes always have a Location header.
        if (!hasLocation && response.StatusCode is 301 or 302 or 303 or 307 or 308)
        {
            hasLocation = true;
        }

        if (hasLocation)
        {
            headers.Add(new OpenApiResponseHeaderModel(
                "Location",
                "The URL of the created or redirected resource."));
        }

        if (hasContentDisposition)
        {
            headers.Add(new OpenApiResponseHeaderModel(
                "Content-Disposition",
                "Indicates the file should be downloaded as an attachment, including the suggested filename."));
        }

        return headers.Count == 0
            ? ImmutableArray<OpenApiResponseHeaderModel>.Empty
            : headers.ToImmutableArray();
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
/// <param name="Headers">Well-known response headers inferred from result types and status codes.</param>
public sealed record OpenApiResponseObjectModel(
    int StatusCode,
    bool HasResponseBody,
    ImmutableArray<string> ResultTypeNames,
    ImmutableArray<OpenApiResponseContentModel> Content,
    ImmutableArray<OpenApiResponseHeaderModel> Headers = default);

/// <summary>
/// Captures one rendered media-type entry for an OpenAPI-style response object.
/// </summary>
/// <param name="MediaType">The response media type key.</param>
/// <param name="SchemaComponents">The schema components referenced by this content entry.</param>
/// <param name="Discriminator">Optional discriminator metadata when the response schema represents a same-status union.</param>
/// <param name="InlineSchema">Optional inline schema for non-JSON body types (e.g. plain text, binary) that have no reusable component.</param>
public sealed record OpenApiResponseContentModel(
    string MediaType,
    ImmutableArray<OpenApiResponseContentSchemaComponent> SchemaComponents,
    OpenApiResponseDiscriminatorModel? Discriminator = null,
    OpenApiInlineSchemaModel? InlineSchema = null);

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

/// <summary>
/// Represents a simple inline OpenAPI schema for non-JSON body responses such as plain text
/// (<c>text/plain</c>) and binary streams (<c>application/octet-stream</c>) that do not
/// require a reusable <c>#/components/schemas</c> reference.
/// </summary>
/// <param name="Type">The OpenAPI schema type (e.g. <c>string</c>).</param>
/// <param name="Format">Optional OpenAPI schema format (e.g. <c>binary</c>).</param>
public sealed record OpenApiInlineSchemaModel(
    string Type,
    string? Format = null);

/// <summary>
/// Represents a response header declared in an OpenAPI response object. Blueprint infers well-known
/// headers from result types — for example <c>Location</c> on 201 Created, <c>ETag</c> and
/// <c>Cache-Control</c> on responses that can carry conditional caching metadata, and
/// <c>Content-Disposition</c> on file-stream responses.
/// </summary>
/// <param name="Name">The HTTP header name (e.g. <c>Location</c>, <c>ETag</c>).</param>
/// <param name="Description">A human-readable description of the header's purpose.</param>
/// <param name="SchemaType">The OpenAPI schema type for the header value (typically <c>string</c>).</param>
public sealed record OpenApiResponseHeaderModel(
    string Name,
    string Description,
    string SchemaType = "string");
