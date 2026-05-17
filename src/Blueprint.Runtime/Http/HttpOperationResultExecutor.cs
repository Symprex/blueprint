using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;
using MvcValidationProblemDetails = Microsoft.AspNetCore.Mvc.ValidationProblemDetails;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Translates supported semantic Blueprint operation results into HTTP responses without relying on self-executing result implementations.
/// </summary>
public class HttpOperationResultExecutor
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string ProblemJsonContentType = "application/problem+json; charset=utf-8";
    private const string PlainTextContentType = "text/plain; charset=utf-8";

    // Cached empty options for the rare case where no app-owned serializer context exists.
    private static readonly JsonSerializerOptions _emptyFallbackOptions = new();

    // Pre-built ProblemDetails for fixed error responses, avoiding per-request allocation.
    // These are safe to share because JsonSerializer reads but does not mutate them.
    private static readonly MvcProblemDetails _unauthorizedProblem = new()
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Unauthorized",
        Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
        Detail = "Authentication is required to access this resource.",
    };

    private static readonly MvcProblemDetails _forbiddenProblem = new()
    {
        Status = StatusCodes.Status403Forbidden,
        Title = "Forbidden",
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        Detail = "You do not have permission to access this resource.",
    };

    private static readonly MvcProblemDetails _unsupportedMediaTypeProblem = new()
    {
        Status = StatusCodes.Status415UnsupportedMediaType,
        Title = "Unsupported Media Type",
        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.13",
        Detail = "The request content type is not supported. Expected 'application/json'.",
    };

    private static readonly BlueprintHttpOptions _defaultHttpOptions = new();

    // Thread-local ArrayBufferWriter to avoid per-request allocation for resource event envelope
    // serialization. Cleared after each use so the buffer is reusable within the same thread.
    [ThreadStatic]
    private static System.Buffers.ArrayBufferWriter<byte>? _threadLocalBuffer;

    // Thread-local Utf8JsonWriter to eliminate the per-request class allocation (~96 B).
    // Reset via Utf8JsonWriter.Reset(IBufferWriter<byte>) before each use (.NET 8+ API).
    [ThreadStatic]
    private static Utf8JsonWriter? _threadLocalWriter;

    /// <summary>
    /// Attempts to execute the supplied semantic result against the HTTP response.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="operationContext">The Blueprint operation context for the current execution.</param>
    /// <param name="result">The semantic result to translate.</param>
    /// <param name="httpOptions">The active HTTP host options for the current mapping.</param>
    /// <returns><see langword="true"/> when the result was translated by this executor; otherwise <see langword="false"/>.</returns>
    public async Task<bool> TryExecuteAsync(HttpContext httpContext, ApiOperationContext operationContext, OperationResult result, BlueprintHttpOptions? httpOptions = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(operationContext);
        ArgumentNullException.ThrowIfNull(result);

        httpOptions ??= _defaultHttpOptions;

        switch (result)
        {
            case UnauthenticatedResult:
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                httpContext.Response.ContentType = ProblemJsonContentType;
                await WriteJsonAsync(httpContext, operationContext, _unauthorizedProblem).ConfigureAwait(false);
                return true;
            case ForbiddenResult:
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                httpContext.Response.ContentType = ProblemJsonContentType;
                await WriteJsonAsync(httpContext, operationContext, _forbiddenProblem).ConfigureAwait(false);
                return true;
            case BindingFailureResult bindingFailureResult:
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentType = ProblemJsonContentType;
                await WriteJsonAsync(httpContext, operationContext, new MvcProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Bad Request",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Detail = bindingFailureResult.Reason,
                }).ConfigureAwait(false);
                return true;
            case UnsupportedMediaTypeResult:
                httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                httpContext.Response.ContentType = ProblemJsonContentType;
                await WriteJsonAsync(httpContext, operationContext, _unsupportedMediaTypeProblem).ConfigureAwait(false);
                return true;
            case HttpPipelineShortCircuitResult:
                // Response was already written by the generated pipeline (e.g. binding failure).
                return true;
            case CreatedResult createdResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                httpContext.Response.ContentType = JsonContentType;
                TrySetLocationHeader(httpContext, createdResult.Content);
                await WriteJsonAsync(httpContext, operationContext, createdResult.Content).ConfigureAwait(false);
                return true;
            case AcceptedResult acceptedResult:
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                httpContext.Response.ContentType = JsonContentType;
                await WriteJsonAsync(httpContext, operationContext, acceptedResult.Content).ConfigureAwait(false);
                return true;
            case ConflictResult conflictResult:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                httpContext.Response.ContentType = JsonContentType;
                await WriteJsonAsync(httpContext, operationContext, conflictResult.Content).ConfigureAwait(false);
                return true;
            case NotFoundResult notFoundResult:
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                httpContext.Response.ContentType = JsonContentType;
                await WriteJsonAsync(httpContext, operationContext, notFoundResult.Content).ConfigureAwait(false);
                return true;
            case OkResult okResult:
                if (okResult is INotModifiedResult notModifiedResult)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status304NotModified;
                    if (okResult.HasHeaders)
                    {
                        foreach (var header in okResult.Headers)
                        {
                            httpContext.Response.Headers[header.Key] = header.Value;
                        }
                    }

                    return true;
                }

                if (okResult.Content is null)
                {
                    throw new InvalidOperationException("OkResult must not carry null content when translated by the HTTP executor.");
                }

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = JsonContentType;
                if (okResult.HasHeaders)
                {
                    foreach (var header in okResult.Headers)
                    {
                        httpContext.Response.Headers[header.Key] = header.Value;
                    }
                }

                await WriteJsonAsync(httpContext, operationContext, okResult.Content).ConfigureAwait(false);
                return true;
            case ValidationFailedResult validationFailedResult when DescriptorSupportsFailureResult(operationContext.Descriptor, validationFailedResult):
                await WriteValidationFailureAsync(httpContext, operationContext, validationFailedResult).ConfigureAwait(false);
                return true;
            case UnhandledExceptionResult unhandledExceptionResult when DescriptorSupportsFailureResult(operationContext.Descriptor, unhandledExceptionResult):
                await WriteUnhandledExceptionAsync(httpContext, operationContext, unhandledExceptionResult, httpOptions).ConfigureAwait(false);
                return true;
            case NoResultOperationResult:
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                return true;
            case KeyOnlyResourceEventResult keyOnlyEvent:
                httpContext.Response.StatusCode = MapChangeTypeToStatusCode(keyOnlyEvent.ChangeType);
                return true;
            case ResourceEventResult resourceEventResult:
                httpContext.Response.StatusCode = MapChangeTypeToStatusCode(resourceEventResult.ChangeType);
                httpContext.Response.ContentType = JsonContentType;
                if (resourceEventResult.ChangeType == ChangeType.Created)
                {
                    TrySetLocationHeader(httpContext, resourceEventResult.Content);
                }

                await WriteResourceEventEnvelopeAsync(httpContext, operationContext, resourceEventResult).ConfigureAwait(false);
                return true;
            case HttpNoContentResult:
                httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
                return true;
            case HttpAcceptedResult:
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                return true;
            case PlainTextResult plainTextResult:
                httpContext.Response.StatusCode = (int)plainTextResult.StatusCode;
                httpContext.Response.ContentType = string.Equals(plainTextResult.ContentType, "text/plain", StringComparison.Ordinal)
                    ? PlainTextContentType
                    : $"{plainTextResult.ContentType}; charset=utf-8";
                await httpContext.Response.WriteAsync(plainTextResult.Content, httpContext.RequestAborted).ConfigureAwait(false);
                return true;
            case RedirectResult redirectResult:
                httpContext.Response.StatusCode = redirectResult.StatusCode;
                httpContext.Response.Headers["Location"] = redirectResult.Url;
                return true;
            case FileStreamResult fileStreamResult:
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = fileStreamResult.ContentType;
                if (fileStreamResult.FileName is not null)
                {
                    httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileStreamResult.FileName}\"";
                }

                await fileStreamResult.Stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted).ConfigureAwait(false);
                return true;
            case StatusCodeResult statusCodeResult:
                httpContext.Response.StatusCode = statusCodeResult.StatusCode;
                return true;
            case ResourceEvent resourceEvent:
                httpContext.Response.StatusCode = MapResourceEventChangeTypeToStatusCode(resourceEvent.ChangeType);
                httpContext.Response.ContentType = JsonContentType;
                if (resourceEvent.ChangeType == ResourceEventChangeType.Created && resourceEvent.Data is not null)
                {
                    TrySetLocationHeader(httpContext, resourceEvent.Data);
                }

                await WriteJsonAsync(httpContext, operationContext, resourceEvent).ConfigureAwait(false);
                return true;
            case HttpResult httpResult:
                await httpResult.ExecuteAsync(operationContext).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Per-type cache key that binds a content <see cref="Type"/> to a specific
    /// <see cref="JsonSerializerContext"/> so the <see cref="JsonTypeInfo"/> lookup
    /// is performed at most once per (context, type) pair.
    /// </summary>
    private readonly record struct TypeInfoCacheKey(JsonSerializerContext? Context, Type ContentType);

    private static readonly ConcurrentDictionary<TypeInfoCacheKey, JsonTypeInfo?> _typeInfoCache = new();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The HTTP executor prefers the app-owned serializer context when available and only falls back to runtime serialization for uncovered response bodies while the broader serializer-coverage migration remains in progress.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Fallback serialization path uses runtime JSON serialization when the app-owned serializer context does not cover the response type. AOT consumers must ensure their JsonSerializerContext covers all response types.")]
    private static async Task WriteJsonAsync(HttpContext httpContext, ApiOperationContext operationContext, object content)
    {
        var serializerContext = operationContext.App.AppOwnedSerializerContext;
        var contentType = content.GetType();

        var jsonTypeInfo = _typeInfoCache.GetOrAdd(
            new TypeInfoCacheKey(serializerContext, contentType),
            static key => key.Context?.GetTypeInfo(key.ContentType));

        if (jsonTypeInfo is not null)
        {
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, content, jsonTypeInfo, httpContext.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            var serializerOptions = serializerContext?.Options ?? _emptyFallbackOptions;
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, content, contentType, CreateFallbackSerializerOptions(serializerOptions), httpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    // Pre-computed ChangeType enum strings matching JsonStringEnumConverter<ChangeType> output (PascalCase).
    private static readonly string[] _changeTypeStrings = [nameof(ChangeType.Created), nameof(ChangeType.Updated), nameof(ChangeType.Deleted)];

    // Pre-encoded JSON property names for ResourceEvent envelope to avoid per-request UTF-8 conversion.
    private static readonly JsonEncodedText _propObject = JsonEncodedText.Encode("$object");
    private static readonly JsonEncodedText _propEventId = JsonEncodedText.Encode("eventId");
    private static readonly JsonEncodedText _propChangeType = JsonEncodedText.Encode("changeType");
    private static readonly JsonEncodedText _propCreated = JsonEncodedText.Encode("created");
    private static readonly JsonEncodedText _propResourceObject = JsonEncodedText.Encode("resourceObject");
    private static readonly JsonEncodedText _propHref = JsonEncodedText.Encode("href");
    private static readonly JsonEncodedText _propData = JsonEncodedText.Encode("data");
    private static readonly JsonEncodedText _propMetadata = JsonEncodedText.Encode("metadata");
    private static readonly JsonEncodedText _propSecureData = JsonEncodedText.Encode("secureData");
    private static readonly JsonEncodedText _propChangedValues = JsonEncodedText.Encode("changedValues");
    private static readonly JsonEncodedText _propCorrelationId = JsonEncodedText.Encode("correlationId");
    private static readonly JsonEncodedText _valEvent = JsonEncodedText.Encode("event");

    /// <summary>
    /// Writes a <see cref="ResourceEventResult"/> as an event envelope directly via <see cref="Utf8JsonWriter"/>,
    /// bypassing the private <c>ResourceEventEnvelope</c> wrapper allocation and the reflection-based STJ fallback
    /// that it would trigger (since the wrapper is not registered in any <see cref="JsonSerializerContext"/>).
    /// The inner <c>data</c> payload is serialized via the app's source-generated context when available.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Dictionary<string,object> metadata bags require runtime serialization for heterogeneous values. The data payload prefers the app-owned serializer context and only falls back to reflection for uncovered types.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Fallback serialization for metadata/secureData/changedValues dictionaries uses runtime JSON when the app-owned context does not cover these types. AOT consumers must ensure full type coverage.")]
    private static async Task WriteResourceEventEnvelopeAsync(
        HttpContext httpContext,
        ApiOperationContext operationContext,
        ResourceEventResult result)
    {
        var serializerContext = operationContext.App.AppOwnedSerializerContext;
        var serializerOptions = serializerContext?.Options ?? _emptyFallbackOptions;

        // Reuse a thread-local ArrayBufferWriter to avoid per-request allocation for the
        // resource event envelope (typically < 1KB). The buffer is cleared after each use.
        var buffer = _threadLocalBuffer ??= new System.Buffers.ArrayBufferWriter<byte>(512);
        buffer.Clear();

        // Reuse a thread-local Utf8JsonWriter to avoid per-request class allocation (~96 B).
        var writer = _threadLocalWriter;
        if (writer is null)
        {
            writer = new Utf8JsonWriter(buffer);
            _threadLocalWriter = writer;
        }
        else
        {
            writer.Reset(buffer);
        }

        writer.WriteStartObject();

        writer.WriteString(_propObject, _valEvent);
        writer.WriteString(_propEventId, result.EventId);
        writer.WriteString(_propChangeType, _changeTypeStrings[(int)result.ChangeType]);
        writer.WriteString(_propCreated, DateTimeOffset.UtcNow);
        writer.WriteString(_propResourceObject, ApiResource.GetTypeName(result.Content.GetType()));

        // href: extract from self-link when the resource is linkable
        string? href = null;
        if (result.Content is ILinkableResource { Links: { } links } &&
            links.TryGetValue("self", out var selfLink))
        {
            href = selfLink.Href;
        }

        if (href is not null)
        {
            writer.WriteString(_propHref, href);
        }

        // data: use app-owned source-generated context when available for zero-reflection serialization
        writer.WritePropertyName(_propData);

        var dataType = result.Content.GetType();
        var dataTypeInfo = _typeInfoCache.GetOrAdd(
            new TypeInfoCacheKey(serializerContext, dataType),
            static key => key.Context?.GetTypeInfo(key.ContentType));

        if (dataTypeInfo is not null)
        {
            JsonSerializer.Serialize(writer, result.Content, dataTypeInfo);
        }
        else
        {
            JsonSerializer.Serialize(writer, result.Content, dataType, CreateFallbackSerializerOptions(serializerOptions));
        }

        // Nullable dictionary properties — omit when null to match the prior envelope behavior
        if (result.Metadata is not null)
        {
            writer.WritePropertyName(_propMetadata);
            JsonSerializer.Serialize(writer, result.Metadata, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.SecureData is not null)
        {
            writer.WritePropertyName(_propSecureData);
            JsonSerializer.Serialize(writer, result.SecureData, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.ChangedValues is not null)
        {
            writer.WritePropertyName(_propChangedValues);
            JsonSerializer.Serialize(writer, result.ChangedValues, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.CorrelationId is not null)
        {
            writer.WriteString(_propCorrelationId, result.CorrelationId);
        }

        writer.WriteEndObject();
        writer.Flush();

        httpContext.Response.ContentLength = buffer.WrittenCount;
        await httpContext.Response.Body.WriteAsync(buffer.WrittenMemory, httpContext.RequestAborted).ConfigureAwait(false);
    }

    private static readonly ConcurrentDictionary<JsonSerializerOptions, JsonSerializerOptions> _fallbackOptionsCache = new();

    // Shared singleton avoids allocating a new resolver each time the fallback cache is populated.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The fallback serialization path already suppresses IL2026 for this code path. Sharing a single DefaultJsonTypeInfoResolver instance avoids unnecessary allocations.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "DefaultJsonTypeInfoResolver requires dynamic code. This is only used as a fallback when the app-owned serializer context does not cover a type.")]
    private static readonly DefaultJsonTypeInfoResolver _sharedDefaultTypeInfoResolver = new();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The HTTP executor already relies on runtime serialization for uncovered response roots while the serializer-coverage migration remains in progress. This fallback keeps app-owned source-generated metadata first and only adds reflection metadata for wrapper roots the app context does not declare.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Fallback options use DefaultJsonTypeInfoResolver which requires dynamic code. AOT consumers must ensure their JsonSerializerContext covers all serialized types.")]
    private static JsonSerializerOptions CreateFallbackSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        return _fallbackOptionsCache.GetOrAdd(serializerOptions, static opts =>
            new JsonSerializerOptions(opts)
            {
                TypeInfoResolver = opts.TypeInfoResolver is null
                    ? _sharedDefaultTypeInfoResolver
                    : JsonTypeInfoResolver.Combine(opts.TypeInfoResolver, _sharedDefaultTypeInfoResolver),
            });
    }

    private static readonly ConcurrentDictionary<Type, FrozenSet<string>> _resultTypeNamesCache = new();

    private static bool DescriptorSupportsFailureResult(ApiOperationDescriptor descriptor, OperationResult result)
    {
        if (descriptor.TryGetFeatureData<OperationFailureOutcomesFeatureData>(out var failureOutcomes) == false)
        {
            return false;
        }

        var runtimeResultTypeNames = _resultTypeNamesCache.GetOrAdd(result.GetType(), static resultType =>
        {
            // Count ancestor types first to avoid intermediate List allocation.
            var count = 0;
            for (var current = resultType; current is not null; current = current.BaseType)
            {
                if (current.FullName is not null)
                {
                    count++;
                }
            }

            var names = new string[count];
            var index = 0;
            for (var current = resultType; current is not null; current = current.BaseType)
            {
                if (current.FullName is { } fullName)
                {
                    names[index++] = fullName;
                }
            }

            return names.ToFrozenSet(StringComparer.Ordinal);
        });

        var outcomes = failureOutcomes.Outcomes;

        for (var i = 0; i < outcomes.Length; i++)
        {
            if (runtimeResultTypeNames.Contains(outcomes[i].ResultTypeName))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteValidationFailureAsync(HttpContext httpContext, ApiOperationContext operationContext, ValidationFailedResult validationFailedResult)
    {
        IDictionary<string, string[]> validationErrors;

        if (validationFailedResult.Errors is { Count: > 0 } preBuilt)
        {
            // The concrete type is Dictionary<string, string[]> which implements both
            // IReadOnlyDictionary and IDictionary. Cast directly to avoid a copy.
            // MvcValidationProblemDetails only reads the dictionary so mutation is safe.
            validationErrors = (IDictionary<string, string[]>)preBuilt;
        }
        else
        {
            // Single-pass grouping using List<string> to avoid O(n²) array expansion.
            // Final conversion to string[] happens once per member after all entries are collected.
            var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var validationResult in validationFailedResult.ValidationResults)
            {
                foreach (var entry in ExpandValidationErrorEntries(validationResult))
                {
                    if (!grouped.TryGetValue(entry.MemberName, out var list))
                    {
                        list = new List<string>(1);
                        grouped[entry.MemberName] = list;
                    }

                    if (!list.Contains(entry.ErrorMessage))
                    {
                        list.Add(entry.ErrorMessage);
                    }
                }
            }

            // Convert List<string> → string[] in a single pass for the problem details dictionary.
            var result = new Dictionary<string, string[]>(grouped.Count, StringComparer.Ordinal);

            foreach (var kvp in grouped)
            {
                result[kvp.Key] = kvp.Value.ToArray();
            }

            validationErrors = result;
        }

        var problemDetails = new MvcValidationProblemDetails(validationErrors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "One or more validation errors occurred.",
            Type = "https://tools.ietf.org/html/rfc4918#section-11.2",
        };

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        httpContext.Response.ContentType = ProblemJsonContentType;
        await WriteJsonAsync(httpContext, operationContext, problemDetails).ConfigureAwait(false);
    }

    private static IEnumerable<(string MemberName, string ErrorMessage)> ExpandValidationErrorEntries(ValidationResult validationResult)
    {
        var errorMessage = string.IsNullOrWhiteSpace(validationResult.ErrorMessage)
            ? "Validation failed."
            : validationResult.ErrorMessage;

        // Fast path: most validation results target exactly one member.
        // Avoid LINQ/HashSet/Array overhead for the common single-member case.
        var hasAny = false;
        string? first = null;

        foreach (var memberName in validationResult.MemberNames)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                continue;
            }

            if (!hasAny)
            {
                first = memberName;
                hasAny = true;
                continue;
            }

            // Multiple members: fall back to full deduplication
            return ExpandMultipleMemberNames(validationResult.MemberNames, first!, errorMessage);
        }

        if (!hasAny)
        {
            return [(string.Empty, errorMessage)];
        }

        return [(first!, errorMessage)];
    }

    private static IEnumerable<(string MemberName, string ErrorMessage)> ExpandMultipleMemberNames(
        IEnumerable<string> memberNames,
        string first,
        string errorMessage)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { first };
        yield return (first, errorMessage);

        foreach (var memberName in memberNames)
        {
            if (!string.IsNullOrWhiteSpace(memberName) && memberName != first && seen.Add(memberName))
            {
                yield return (memberName, errorMessage);
            }
        }
    }

    private static async Task WriteUnhandledExceptionAsync(HttpContext httpContext, ApiOperationContext operationContext, UnhandledExceptionResult unhandledExceptionResult, BlueprintHttpOptions httpOptions)
    {
        var exposeDetails = httpOptions.ExposeExceptionDetailsInErrorResponses;
        var apiEx = unhandledExceptionResult.Exception as ApiException;

        var statusCode = apiEx?.HttpStatus ?? StatusCodes.Status500InternalServerError;

        var problemDetails = new MvcProblemDetails
        {
            Status = statusCode,
            Title = apiEx?.Title ?? (statusCode >= 500 ? "Server error" : "Request error"),
            Type = apiEx?.Type,
            Detail = exposeDetails
                ? apiEx?.Detail ?? unhandledExceptionResult.Exception.Message
                : statusCode >= 500
                    ? "An unexpected error occurred."
                    : apiEx?.Detail ?? unhandledExceptionResult.Exception.Message,
        };

        // Carry over any extension data from the ApiException into the ProblemDetails response.
        if (apiEx?.Extensions is { Count: > 0 })
        {
            foreach (var kvp in apiEx.Extensions)
            {
                problemDetails.Extensions[kvp.Key] = kvp.Value;
            }
        }

        if (exposeDetails && statusCode >= 500)
        {
            problemDetails.Extensions["stackTrace"] = unhandledExceptionResult.Exception.ToString();
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = ProblemJsonContentType;
        await WriteJsonAsync(httpContext, operationContext, problemDetails).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to extract a self-link URL from the content and set it as the Location response header,
    /// which is required for HTTP 201 Created responses per RFC 7231 §6.3.2.
    /// </summary>
    private static void TrySetLocationHeader(HttpContext httpContext, object content)
    {
        if (content is ILinkableResource { Links: { } links } && links.TryGetValue("self", out var selfLink))
        {
            httpContext.Response.Headers.Location = selfLink.Href;
        }
    }

    private static int MapChangeTypeToStatusCode(ChangeType changeType)
    {
        return changeType switch
        {
            ChangeType.Created => StatusCodes.Status201Created,
            ChangeType.Updated => StatusCodes.Status200OK,
            ChangeType.Deleted => StatusCodes.Status204NoContent,
            _ => StatusCodes.Status200OK,
        };
    }

    private static int MapResourceEventChangeTypeToStatusCode(ResourceEventChangeType changeType)
    {
        return changeType switch
        {
            ResourceEventChangeType.Created => StatusCodes.Status201Created,
            ResourceEventChangeType.Updated => StatusCodes.Status200OK,
            ResourceEventChangeType.Deleted => StatusCodes.Status204NoContent,
            _ => StatusCodes.Status200OK,
        };
    }
}
