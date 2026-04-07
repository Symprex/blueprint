using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

        httpOptions ??= new BlueprintHttpOptions();

        switch (result)
        {
            case UnauthenticatedResult:
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return true;
            case BindingFailureResult:
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return true;
            case UnsupportedMediaTypeResult:
                httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return true;
            case HttpPipelineShortCircuitResult:
                // Response was already written by the generated pipeline (e.g. binding failure).
                return true;
            case CreatedResult createdResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(httpContext, operationContext, createdResult.Content);
                return true;
            case AcceptedResult acceptedResult:
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(httpContext, operationContext, acceptedResult.Content);
                return true;
            case ConflictResult conflictResult:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(httpContext, operationContext, conflictResult.Content);
                return true;
            case NotFoundResult notFoundResult:
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(httpContext, operationContext, notFoundResult.Content);
                return true;
            case OkResult okResult:
                if (okResult.Content is null)
                {
                    throw new InvalidOperationException("OkResult must not carry null content when translated by the HTTP executor.");
                }

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(httpContext, operationContext, okResult.Content);
                return true;
            case ValidationFailedResult validationFailedResult when DescriptorSupportsFailureResult(operationContext.Descriptor, validationFailedResult):
                await WriteValidationFailureAsync(httpContext, operationContext, validationFailedResult);
                return true;
            case UnhandledExceptionResult unhandledExceptionResult when DescriptorSupportsFailureResult(operationContext.Descriptor, unhandledExceptionResult):
                await WriteUnhandledExceptionAsync(httpContext, operationContext, unhandledExceptionResult, httpOptions);
                return true;
            case NoResultOperationResult:
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                return true;
            case KeyOnlyResourceEventResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                return true;
            case ResourceEventResult resourceEventResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(httpContext, operationContext, ResourceEventEnvelope.Create(resourceEventResult));
                return true;
            case HttpNoContentResult:
                httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
                return true;
            default:
                return false;
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The HTTP executor prefers the app-owned serializer context when available and only falls back to runtime serialization for uncovered response bodies while the broader serializer-coverage migration remains in progress.")]
    private static async Task WriteJsonAsync(HttpContext httpContext, ApiOperationContext operationContext, object content)
    {
        var serializerContext = operationContext.App.AppOwnedSerializerContext;
        var serializerOptions = serializerContext?.Options ?? new JsonSerializerOptions();
        var json = serializerContext?.GetTypeInfo(content.GetType()) is JsonTypeInfo jsonTypeInfo
            ? JsonSerializer.Serialize(content, jsonTypeInfo)
            : JsonSerializer.Serialize(content, content.GetType(), CreateFallbackSerializerOptions(serializerOptions));

        await httpContext.Response.WriteAsync(json, httpContext.RequestAborted);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The HTTP executor already relies on runtime serialization for uncovered response roots while the serializer-coverage migration remains in progress. This fallback keeps app-owned source-generated metadata first and only adds reflection metadata for wrapper roots the app context does not declare.")]
    private static JsonSerializerOptions CreateFallbackSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        var fallbackOptions = new JsonSerializerOptions(serializerOptions)
        {
            TypeInfoResolver = serializerOptions.TypeInfoResolver is null
            ? new DefaultJsonTypeInfoResolver()
            : JsonTypeInfoResolver.Combine(serializerOptions.TypeInfoResolver, new DefaultJsonTypeInfoResolver()),
        };
        return fallbackOptions;
    }

    private static bool DescriptorSupportsFailureResult(ApiOperationDescriptor descriptor, OperationResult result)
    {
        if (descriptor.TryGetFeatureData<OperationFailureOutcomesFeatureData>(out var failureOutcomes) == false)
        {
            return false;
        }

        var runtimeResultTypeNames = GetResultTypeNames(result.GetType());
        return failureOutcomes.Outcomes.Any(outcome => runtimeResultTypeNames.Contains(outcome.ResultTypeName));
    }

    private static HashSet<string> GetResultTypeNames(Type resultType)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var current = resultType; current is not null; current = current.BaseType)
        {
            if (current.FullName is { } fullName)
            {
                names.Add(fullName);
            }
        }

        return names;
    }

    private static async Task WriteValidationFailureAsync(HttpContext httpContext, ApiOperationContext operationContext, ValidationFailedResult validationFailedResult)
    {
        var validationErrors = validationFailedResult.ValidationResults
            .SelectMany(static validationResult => ExpandValidationErrorEntries(validationResult))
            .GroupBy(static entry => entry.MemberName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static entry => entry.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var problemDetails = new MvcValidationProblemDetails(validationErrors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
        };

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        await WriteJsonAsync(httpContext, operationContext, problemDetails);
    }

    private static IEnumerable<(string MemberName, string ErrorMessage)> ExpandValidationErrorEntries(ValidationResult validationResult)
    {
        var errorMessage = string.IsNullOrWhiteSpace(validationResult.ErrorMessage)
            ? "Validation failed."
            : validationResult.ErrorMessage;
        var memberNames = validationResult.MemberNames
            .Where(static memberName => string.IsNullOrWhiteSpace(memberName) == false)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (memberNames.Length == 0)
        {
            yield return (string.Empty, errorMessage);
            yield break;
        }

        foreach (var memberName in memberNames)
        {
            yield return (memberName, errorMessage);
        }
    }

    private static async Task WriteUnhandledExceptionAsync(HttpContext httpContext, ApiOperationContext operationContext, UnhandledExceptionResult unhandledExceptionResult, BlueprintHttpOptions httpOptions)
    {
        var exposeDetails = httpOptions.ExposeExceptionDetailsInErrorResponses;
        var problemDetails = new MvcProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Server error",
            Detail = exposeDetails
                ? unhandledExceptionResult.Exception.Message
                : "An unexpected error occurred.",
        };

        if (exposeDetails)
        {
            problemDetails.Extensions["stackTrace"] = unhandledExceptionResult.Exception.ToString();
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        await WriteJsonAsync(httpContext, operationContext, problemDetails);
    }

    private sealed class ResourceEventEnvelope
    {
        [JsonPropertyName("eventId")]
        public required string EventId { get; init; }

        [JsonPropertyName("resourceId")]
        public required string ResourceId { get; init; }

        [JsonPropertyName("content")]
        public required object Content { get; init; }

        public static ResourceEventEnvelope Create(ResourceEventResult result)
        {
            return new ResourceEventEnvelope
            {
                EventId = result.EventId,
                ResourceId = result.ResourceId,
                Content = result.Content,
            };
        }
    }
}
