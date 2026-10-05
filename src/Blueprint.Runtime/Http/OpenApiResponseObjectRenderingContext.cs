using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
    /// The property a response made only of legacy resource events is discriminated by: the event's <c>resourceObject</c>.
    /// </summary>
    internal const string ResourceObjectDiscriminatorPropertyName = "resourceObject";

    /// <summary>
    /// Creates deterministic OpenAPI-style response objects from the supplied response-schema reference projection.
    /// This seam intentionally stays response-only: it preserves the current response metadata and only adds JSON content
    /// bindings for covered schema components, leaving operation/document assembly to later slices.
    /// </summary>
    /// <param name="responseSchemaProjection">The response-schema reference projection to render.</param>
    /// <param name="usePlainJsonForErrorResponses">
    /// When <see langword="true"/>, error responses use <c>application/json</c> instead of <c>application/problem+json</c>.
    /// </param>
    /// <returns>The rendered response-object context.</returns>
    public static OpenApiResponseObjectRenderingContext Create(
        OpenApiResponseSchemaReferenceProjectionContext responseSchemaProjection,
        bool usePlainJsonForErrorResponses = false)
    {
        ArgumentNullException.ThrowIfNull(responseSchemaProjection);

        var schemaComponents = OpenApiSchemaComponentRenderingContext.Create(responseSchemaProjection.SchemaProjection);
        var componentsByBodyTypeName = schemaComponents.Components
            .ToFrozenDictionary(static component => component.BodyTypeName, StringComparer.Ordinal);
        var operations = responseSchemaProjection.Operations
            .Select(operation => CreateOperationContext(operation, componentsByBodyTypeName, usePlainJsonForErrorResponses))
            .ToImmutableArray();

        return new OpenApiResponseObjectRenderingContext(responseSchemaProjection, schemaComponents, operations);
    }

    private static OpenApiResponseObjectOperationContext CreateOperationContext(
        OpenApiResponseSchemaReferenceOperationContext operation,
        FrozenDictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName,
        bool usePlainJsonForErrors)
    {
        var responses = operation.Responses
            .Select(response => CreateResponseObject(operation.Operation, response, componentsByBodyTypeName, usePlainJsonForErrors))
            .ToImmutableArray();

        return new OpenApiResponseObjectOperationContext(operation.Operation, responses);
    }

    private static OpenApiResponseObjectModel CreateResponseObject(
        ApiOperationDescriptor operation,
        OpenApiResponseSchemaReferenceResponseContext response,
        FrozenDictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName,
        bool usePlainJsonForErrors)
    {
        var content = response.Response.HasResponseBody
            ? CreateContent(operation, response, componentsByBodyTypeName, usePlainJsonForErrors)
            : ImmutableArray<OpenApiResponseContentModel>.Empty;

        var headers = InferResponseHeaders(response.Response);

        return new OpenApiResponseObjectModel(
            response.Response.StatusCode,
            response.Response.HasResponseBody,
            response.Response.ResultTypeNames,
            content,
            headers,
            response.Response.ProblemExamples);
    }

    private static ImmutableArray<OpenApiResponseContentModel> CreateContent(
        ApiOperationDescriptor operation,
        OpenApiResponseSchemaReferenceResponseContext response,
        FrozenDictionary<string, OpenApiSchemaComponentModel> componentsByBodyTypeName,
        bool usePlainJsonForErrors)
    {
        var schemaComponents = response.SchemaReferences
            .Select(reference => TryCreateContentSchemaComponent(reference, componentsByBodyTypeName))
            .OfType<OpenApiResponseContentSchemaComponent>()
            .ToImmutableArray();
        var discriminator = TryCreateResourceEventDiscriminator(operation, response.Response, schemaComponents);
        var isResourceEventEnvelope = discriminator is null
                                      && (IsResourceEventResultResponse(response.Response) || ReturnsResourceEvent(operation, response.Response));

        return schemaComponents.IsEmpty
            ? TryCreateNonJsonContent(response.Response)
            : [new OpenApiResponseContentModel(SelectMediaType(schemaComponents, usePlainJsonForErrors), schemaComponents, discriminator, InlineSchema: null, isResourceEventEnvelope)];
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
    /// When <paramref name="usePlainJson"/> is <see langword="true"/>, always returns <c>application/json</c>.
    /// </summary>
    private static string SelectMediaType(ImmutableArray<OpenApiResponseContentSchemaComponent> schemaComponents, bool usePlainJson)
    {
        if (usePlainJson || schemaComponents.IsEmpty)
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
        if (schemaComponents.Length < 2)
        {
            return null;
        }

        var schemaComponentsByBodyTypeName = schemaComponents
            .ToFrozenDictionary(static component => component.BodyTypeName, StringComparer.Ordinal);

        // A legacy event's eventId is "{resource}.{sub-id}", the sub-id its handler's to choose at run time, not its
        // resource's [ResourceEvent] id, so a response one of them makes is told apart by the resourceObject or not at all.
        var legacyEventMembers = ResolveLegacyResourceEventMembers(operation, response);
        if (legacyEventMembers.Any(static member => member is not null))
        {
            return TryCreateLegacyResourceEventDiscriminator(legacyEventMembers, schemaComponentsByBodyTypeName, schemaComponents.Length);
        }

        if (operation.TryGetFeatureData<OperationResourceEventMetadataFeatureData>(out var metadata) == false)
        {
            return null;
        }

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

    /// <summary>
    /// Discriminates a response made only of legacy <see cref="Blueprint.Http.ResourceEvent{TResource}"/> members by the
    /// <c>resourceObject</c> the host writes for each: its resource's type name (<see cref="ApiResource.GetTypeName"/>),
    /// fixed by the event's type, as legacy Blueprint set it (Signature365 8b40ecc961, Blueprint.Http/ResourceEvent.cs:38,
    /// :59). Returns <see langword="null"/> when a member is not a legacy event, when two resources share a type name — the
    /// host would write the same value for either — or when the members do not cover the response's components.
    /// </summary>
    private static OpenApiResponseDiscriminatorModel? TryCreateLegacyResourceEventDiscriminator(
        Type?[] legacyEventMembers,
        FrozenDictionary<string, OpenApiResponseContentSchemaComponent> schemaComponentsByBodyTypeName,
        int schemaComponentCount)
    {
        var distinctResourceObjects = new HashSet<string>(StringComparer.Ordinal);
        var cases = new List<OpenApiResponseDiscriminatorCaseModel>(legacyEventMembers.Length);
        foreach (var member in legacyEventMembers)
        {
            if (member is null
                || GetLegacyEventResourceType(member) is not { } resourceType
                || !schemaComponentsByBodyTypeName.TryGetValue(NormalizeBodyTypeName(OpenApiSchemaProjectionContext.FormatFullNameDisplayStyle(resourceType)), out var schemaComponent))
            {
                return null;
            }

            var resourceObject = ApiResource.GetTypeName(resourceType);
            if (!distinctResourceObjects.Add(resourceObject))
            {
                return null;
            }

            cases.Add(new OpenApiResponseDiscriminatorCaseModel(resourceObject, schemaComponent));
        }

        return cases.Count != schemaComponentCount
            ? null
            : new OpenApiResponseDiscriminatorModel(ResourceObjectDiscriminatorPropertyName, cases.ToImmutableArray());
    }

    /// <summary>
    /// Resolves each of the response's result types to the member of the operation's declared <c>Results</c> union it
    /// names, where that member is a legacy <see cref="Blueprint.Http.ResourceEvent"/>, and to <see langword="null"/>
    /// otherwise. The names are the generator's (Roslyn's display names), so each member is named the same way.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Operation types' interfaces are preserved by the source generator's generated pipeline code.")]
    private static Type?[] ResolveLegacyResourceEventMembers(ApiOperationDescriptor operation, OperationHttpResponseMetadata response)
    {
        var declaredResultType = operation.OperationType.GetInterfaces()
            .FirstOrDefault(static candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IReturn<>))
            ?.GetGenericArguments()[0];
        if (declaredResultType is not { IsGenericType: true } || declaredResultType.GetGenericTypeDefinition() != typeof(Results<,>))
        {
            return new Type?[response.ResultTypeNames.Length];
        }

        var members = declaredResultType.GetGenericArguments();

        return response.ResultTypeNames
            .Select(resultTypeName => members.FirstOrDefault(member =>
                typeof(Blueprint.Http.ResourceEvent).IsAssignableFrom(member)
                && string.Equals(OpenApiSchemaProjectionContext.FormatFullNameDisplayStyle(member), resultTypeName, StringComparison.Ordinal)))
            .ToArray();
    }

    /// <summary>
    /// The resource type of a legacy <see cref="Blueprint.Http.ResourceEvent{TResource}"/>, or <see langword="null"/> for
    /// the non-generic <see cref="Blueprint.Http.ResourceEvent"/>.
    /// </summary>
    private static Type? GetLegacyEventResourceType(Type eventType)
    {
        for (var current = eventType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Blueprint.Http.ResourceEvent<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private const string CreatedResultTypeName = "Blueprint.Runtime.Exec.CreatedResult";
    private const string ResourceEventCreatedResultPrefix = "Blueprint.Runtime.Exec.ResourceEventResult+";
    private const string ResourceEventResultPrefix = "Blueprint.Runtime.Exec.ResourceEventResult";
    private const string RedirectResultTypeName = "Blueprint.Runtime.Http.RedirectResult";
    private const string OkResultTypeName = "Blueprint.Runtime.Exec.OkResult";

    /// <summary>
    /// Returns <see langword="true"/> when any result type contributing to this response is a
    /// <c>ResourceEventResult</c> variant. The HTTP executor wraps these in a <c>ResourceEventEnvelope</c>
    /// at runtime, so the OpenAPI schema must describe the envelope shape rather than the bare inner type.
    /// </summary>
    private static bool IsResourceEventResultResponse(OperationHttpResponseMetadata response)
    {
        foreach (var resultTypeName in response.ResultTypeNames)
        {
            // Match "Blueprint.Runtime.Exec.ResourceEventResult" exactly or as a generic prefix
            // (e.g., "Blueprint.Runtime.Exec.ResourceEventResult<...>")
            if (resultTypeName.StartsWith(ResourceEventResultPrefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when this is the success response of an operation whose handler returns a
    /// <see cref="Blueprint.Http.ResourceEvent"/> — the legacy result type, a subclass of it included. The HTTP host writes
    /// the whole event for it, as legacy Blueprint did, so the schema must describe the envelope rather than the event's
    /// data, which is what the response metadata names as its body.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Operation types' interfaces are preserved by the source generator's generated pipeline code.")]
    private static bool ReturnsResourceEvent(ApiOperationDescriptor operation, OperationHttpResponseMetadata response)
    {
        if (!response.HasResponseBody || response.StatusCode is < 200 or > 299)
        {
            return false;
        }

        var declaredResultType = operation.OperationType.GetInterfaces()
            .FirstOrDefault(static candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IReturn<>))
            ?.GetGenericArguments()[0];

        return declaredResultType is not null && typeof(Blueprint.Http.ResourceEvent).IsAssignableFrom(declaredResultType);
    }

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
/// <param name="ProblemExamples">
/// The title and type of each <c>ApiExceptionFactory</c> the operation declares at this status
/// (<see cref="OperationHttpResponseMetadata.ProblemExamples"/>). Default or empty when there are none.
/// </param>
public sealed record OpenApiResponseObjectModel(
    int StatusCode,
    bool HasResponseBody,
    ImmutableArray<string> ResultTypeNames,
    ImmutableArray<OpenApiResponseContentModel> Content,
    ImmutableArray<OpenApiResponseHeaderModel> Headers = default,
    ImmutableArray<OperationProblemExample> ProblemExamples = default)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenApiResponseObjectModel"/> class with no problem examples.
    /// </summary>
    /// <remarks>
    /// The 1.0.2 signature, kept so that a binary built against it (an operation processor that makes a response, for
    /// example) still binds: <see cref="ProblemExamples"/> was added in 1.0.3 as an optional parameter, which a recompile
    /// fills in but an existing binary cannot.
    /// </remarks>
    /// <param name="statusCode">The HTTP status code emitted for this response family.</param>
    /// <param name="hasResponseBody">Whether the response family writes a response body.</param>
    /// <param name="resultTypeNames">The semantic result type names that collapse into this response family.</param>
    /// <param name="content">The rendered response content keyed by media type when covered schema components exist.</param>
    /// <param name="headers">Well-known response headers inferred from result types and status codes.</param>
    public OpenApiResponseObjectModel(
        int statusCode,
        bool hasResponseBody,
        ImmutableArray<string> resultTypeNames,
        ImmutableArray<OpenApiResponseContentModel> content,
        ImmutableArray<OpenApiResponseHeaderModel> headers)
        : this(statusCode, hasResponseBody, resultTypeNames, content, headers, default)
    {
    }

    /// <summary>Deconstructs the response into the five members it had in 1.0.2, kept for binaries built against that signature.</summary>
    /// <param name="statusCode">The HTTP status code emitted for this response family.</param>
    /// <param name="hasResponseBody">Whether the response family writes a response body.</param>
    /// <param name="resultTypeNames">The semantic result type names that collapse into this response family.</param>
    /// <param name="content">The rendered response content keyed by media type when covered schema components exist.</param>
    /// <param name="headers">Well-known response headers inferred from result types and status codes.</param>
    public void Deconstruct(
        out int statusCode,
        out bool hasResponseBody,
        out ImmutableArray<string> resultTypeNames,
        out ImmutableArray<OpenApiResponseContentModel> content,
        out ImmutableArray<OpenApiResponseHeaderModel> headers)
    {
        statusCode = this.StatusCode;
        hasResponseBody = this.HasResponseBody;
        resultTypeNames = this.ResultTypeNames;
        content = this.Content;
        headers = this.Headers;
    }
}

/// <summary>
/// Captures one rendered media-type entry for an OpenAPI-style response object.
/// </summary>
/// <param name="MediaType">The response media type key.</param>
/// <param name="SchemaComponents">The schema components referenced by this content entry.</param>
/// <param name="Discriminator">Optional discriminator metadata when the response schema represents a same-status union.</param>
/// <param name="InlineSchema">Optional inline schema for non-JSON body types (e.g. plain text, binary) that have no reusable component.</param>
/// <param name="IsResourceEventEnvelope">When <see langword="true"/>, the response body is a <c>ResourceEvent</c> envelope
/// wrapping the inner type schema. The rendering layer emits a synthetic envelope schema with <c>data: $ref → inner type</c>
/// instead of a direct <c>$ref</c> to the inner type.</param>
public sealed record OpenApiResponseContentModel(
    string MediaType,
    ImmutableArray<OpenApiResponseContentSchemaComponent> SchemaComponents,
    OpenApiResponseDiscriminatorModel? Discriminator = null,
    OpenApiInlineSchemaModel? InlineSchema = null,
    bool IsResourceEventEnvelope = false);

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
