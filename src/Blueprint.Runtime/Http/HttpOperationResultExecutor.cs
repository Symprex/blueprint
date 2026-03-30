using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Newtonsoft.Json;
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
            case CreatedResult createdResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(createdResult.Content), httpContext.RequestAborted);
                return true;
            case AcceptedResult acceptedResult:
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(acceptedResult.Content), httpContext.RequestAborted);
                return true;
            case ConflictResult conflictResult:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(conflictResult.Content), httpContext.RequestAborted);
                return true;
            case NotFoundResult notFoundResult:
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(notFoundResult.Content), httpContext.RequestAborted);
                return true;
            case OkResult okResult:
                if (okResult.Content is null)
                {
                    throw new InvalidOperationException("OkResult must not carry null content when translated by the HTTP executor.");
                }

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(okResult.Content), httpContext.RequestAborted);
                return true;
            case ValidationFailedResult validationFailedResult when DescriptorSupportsFailureResult(operationContext.Descriptor, validationFailedResult):
                await WriteValidationFailureAsync(httpContext, validationFailedResult);
                return true;
            case UnhandledExceptionResult unhandledExceptionResult when DescriptorSupportsFailureResult(operationContext.Descriptor, unhandledExceptionResult):
                await WriteUnhandledExceptionAsync(httpContext, unhandledExceptionResult, httpOptions);
                return true;
            case NoResultOperationResult:
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                return true;
            case KeyOnlyResourceEventResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                return true;
            case HttpNoContentResult:
                httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
                return true;
            default:
                return false;
        }
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

    private static async Task WriteValidationFailureAsync(HttpContext httpContext, ValidationFailedResult validationFailedResult)
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
        await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(problemDetails), httpContext.RequestAborted);
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

    private static async Task WriteUnhandledExceptionAsync(HttpContext httpContext, UnhandledExceptionResult unhandledExceptionResult, BlueprintHttpOptions httpOptions)
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
        await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(problemDetails), httpContext.RequestAborted);
    }
}
