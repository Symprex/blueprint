using System.Net;
using Blueprint.Runtime.Exec;
using Microsoft.Extensions.Primitives;

namespace Blueprint.Http;

/// <summary>
/// Base class for HTTP-specific operation results that need to set a status code and
/// optional custom response headers. Concrete subclasses — or consumer-defined result
/// types such as a proxying result — inherit from this so the HTTP host can
/// translate them into a well-formed HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// Use this as a return type when the handler must control the HTTP status code and/or
/// headers but does not need a JSON payload. For results that also carry a typed body,
/// prefer <see cref="Blueprint.Runtime.Exec.OkResult{T}"/>,
/// <see cref="Blueprint.Runtime.Exec.AcceptedResult{T}"/>, or
/// <see cref="Blueprint.Runtime.Exec.CreatedResult{T}"/>.
/// </para>
/// </remarks>
public abstract class HttpResult : OperationResult
{
    private Dictionary<string, StringValues>? _headers;

    /// <summary>
    /// Initialises a new instance of the <see cref="HttpResult" /> class.
    /// </summary>
    /// <param name="statusCode">The status code of the response.</param>
    protected HttpResult(HttpStatusCode statusCode)
    {
        this.StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the status code that will be set for the HTTP response this operation result represents.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets a dictionary that can be populated with custom headers to set on the response when this
    /// result is executed.
    /// </summary>
    public Dictionary<string, StringValues> Headers => this._headers ??= new Dictionary<string, StringValues>();

    /// <summary>
    /// Sets the HTTP status code and adds any custom headers that have been added to <see cref="Headers" />.
    /// </summary>
    /// <param name="context">The context of the operation this result is for.</param>
    /// <returns>A <see cref="Task" /> representing the execution of this method.</returns>
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        var httpContext = context.HttpContext;

        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        var response = httpContext.Response;

        response.StatusCode = (int)this.StatusCode;

        if (this._headers is not null)
        {
            foreach (var h in this._headers)
            {
                response.Headers.Append(h.Key, h.Value);
            }
        }

        return Task.CompletedTask;
    }
}
