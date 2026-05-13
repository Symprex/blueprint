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

    // Pre-built ProblemDetails for fixed error responses, avoiding per-request allocation.
    // These are safe to share because JsonSerializer reads but does not mutate them.
    private static readonly MvcProblemDetails _unauthorizedProblem = new()
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Unauthorized",
        Detail = "Authentication is required to access this resource.",
    };

    private static readonly MvcProblemDetails _forbiddenProblem = new()
    {
        Status = StatusCodes.Status403Forbidden,
        Title = "Forbidden",
        Detail = "You do not have permission to access this resource.",
    };

    private static readonly MvcProblemDetails _unsupportedMediaTypeProblem = new()
    {
        Status = StatusCodes.Status415UnsupportedMediaType,
        Title = "Unsupported Media Type",
        Detail = "The request content type is not supported. Expected 'application/json'.",
    };

    private static readonly BlueprintHttpOptions _defaultHttpOptions = new();

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

                await WriteJsonAsync(httpContext, operationContext, ResourceEventEnvelope.Create(resourceEventResult)).ConfigureAwait(false);
                return true;
            case HttpNoContentResult:
                httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
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
            var serializerOptions = serializerContext?.Options ?? new JsonSerializerOptions();
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, content, contentType, CreateFallbackSerializerOptions(serializerOptions), httpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    private static readonly ConcurrentDictionary<JsonSerializerOptions, JsonSerializerOptions> _fallbackOptionsCache = new();

    // Shared singleton avoids allocating a new resolver each time the fallback cache is populated.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The fallback serialization path already suppresses IL2026 for this code path. Sharing a single DefaultJsonTypeInfoResolver instance avoids unnecessary allocations.")]
    private static readonly DefaultJsonTypeInfoResolver _sharedDefaultTypeInfoResolver = new();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The HTTP executor already relies on runtime serialization for uncovered response roots while the serializer-coverage migration remains in progress. This fallback keeps app-owned source-generated metadata first and only adds reflection metadata for wrapper roots the app context does not declare.")]
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
            // Single-pass grouping that builds string[] values directly,
            // avoiding the intermediate Dictionary<string, List<string>> copy.
            var grouped = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var validationResult in validationFailedResult.ValidationResults)
            {
                foreach (var entry in ExpandValidationErrorEntries(validationResult))
                {
                    if (!grouped.TryGetValue(entry.MemberName, out var existing))
                    {
                        grouped[entry.MemberName] = [entry.ErrorMessage];
                    }
                    else if (Array.IndexOf(existing, entry.ErrorMessage) < 0)
                    {
                        var expanded = new string[existing.Length + 1];
                        existing.CopyTo(expanded, 0);
                        expanded[^1] = entry.ErrorMessage;
                        grouped[entry.MemberName] = expanded;
                    }
                }
            }

            validationErrors = grouped;
        }

        var problemDetails = new MvcValidationProblemDetails(validationErrors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
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

    /// <summary>
    /// Wire-format envelope for <see cref="ResourceEventResult"/> responses. The shape intentionally mirrors
    /// the legacy <see cref="ResourceEvent"/> class so that both code paths produce identical JSON, keeping
    /// TypeScript client generators and downstream consumers working without changes.
    /// </summary>
    private sealed class ResourceEventEnvelope
    {
        [JsonPropertyName("$object")]
        public string Object => "event";

        [JsonPropertyName("eventId")]
        public required string EventId { get; init; }

        [JsonPropertyName("changeType")]
        [JsonConverter(typeof(JsonStringEnumConverter<ChangeType>))]
        public required ChangeType ChangeType { get; init; }

        [JsonPropertyName("created")]
        public required DateTimeOffset Created { get; init; }

        [JsonPropertyName("resourceObject")]
        public required string ResourceObject { get; init; }

        [JsonPropertyName("href")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Href { get; init; }

        [JsonPropertyName("data")]
        public required object Data { get; init; }

        [JsonPropertyName("metadata")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? Metadata { get; init; }

        public static ResourceEventEnvelope Create(ResourceEventResult result)
        {
            return new ResourceEventEnvelope
            {
                EventId = result.EventId,
                ChangeType = result.ChangeType,
                Created = DateTimeOffset.UtcNow,
                ResourceObject = ApiResource.GetTypeName(result.Content.GetType()),
                Data = result.Content,
                Metadata = result.Metadata,
            };
        }
    }
}
