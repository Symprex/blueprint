using Blueprint.Runtime.Exec;
using Newtonsoft.Json;

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
    /// <returns><see langword="true"/> when the result was translated by this executor; otherwise <see langword="false"/>.</returns>
    public async Task<bool> TryExecuteAsync(HttpContext httpContext, ApiOperationContext operationContext, OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(operationContext);
        ArgumentNullException.ThrowIfNull(result);

        switch (result)
        {
            case CreatedResult createdResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync(JsonConvert.SerializeObject(createdResult.Content), httpContext.RequestAborted);
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
}
