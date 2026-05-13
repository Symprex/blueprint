using Blueprint.Runtime.Exec;
using Microsoft.Extensions.Primitives;

namespace Blueprint.Http;

/// <summary>
/// An <see cref="HttpResult" /> that proxies a downstream <see cref="HttpResponseMessage" /> directly
/// to the client. The status code, response headers, and body are copied verbatim from the downstream
/// response, making this ideal for gateway and proxy scenarios where an operation forwards a call to
/// another service and returns the raw response.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="HttpResponseMessage" /> is NOT disposed by this result — the caller retains ownership
/// and should dispose it after the pipeline completes (typically via a <c>using</c> declaration in the
/// handler method).
/// </para>
/// <para>
/// Only response headers (not content headers) are forwarded automatically. Content headers such as
/// <c>Content-Type</c> and <c>Content-Length</c> are copied from
/// <see cref="HttpResponseMessage.Content"/> separately to ensure the ASP.NET Core response is well-formed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task&lt;ProxyResult&gt; HandleAsync(LoadAnalyticsQuery operation, HttpClient httpClient)
/// {
///     using var response = await httpClient.GetAsync($"/cubejs-api/v1/load?query={operation.Query}");
///     response.EnsureSuccessStatusCode();
///     return new ProxyResult(response);
/// }
/// </code>
/// </example>
public class ProxyResult : HttpResult
{
    private readonly HttpResponseMessage _downstreamResponse;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyResult" /> class that will proxy the given
    /// downstream <see cref="HttpResponseMessage" /> to the client.
    /// </summary>
    /// <param name="downstreamResponse">The downstream HTTP response to proxy.</param>
    public ProxyResult(HttpResponseMessage downstreamResponse)
        : base(downstreamResponse.StatusCode)
    {
        ArgumentNullException.ThrowIfNull(downstreamResponse);
        this._downstreamResponse = downstreamResponse;
    }

    /// <summary>
    /// Gets the downstream <see cref="HttpResponseMessage" /> that this result will proxy.
    /// </summary>
    public HttpResponseMessage DownstreamResponse => this._downstreamResponse;

    /// <inheritdoc />
    public override async Task ExecuteAsync(ApiOperationContext context)
    {
        // Set status code and any manually-added headers from the base class
        await base.ExecuteAsync(context).ConfigureAwait(false);

        var httpContext = context.HttpContext;

        if (httpContext is null)
        {
            return;
        }

        var response = httpContext.Response;

        // Forward response headers (e.g. Cache-Control, ETag, X-* custom headers)
        foreach (var (key, values) in this._downstreamResponse.Headers)
        {
            response.Headers[key] = new StringValues([.. values]);
        }

        // Forward content headers (Content-Type, Content-Length, Content-Encoding, etc.)
        if (this._downstreamResponse.Content is not null)
        {
            foreach (var (key, values) in this._downstreamResponse.Content.Headers)
            {
                response.Headers[key] = new StringValues([.. values]);
            }

            await this._downstreamResponse.Content.CopyToAsync(response.Body).ConfigureAwait(false);
        }
    }
}
