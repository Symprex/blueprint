using Blueprint.Runtime.Exec;
using Microsoft.Extensions.Primitives;

namespace Blueprint.Http;

/// <summary>
/// An <see cref="HttpResult" /> that proxies a downstream <see cref="HttpResponseMessage" /> directly
/// to the client. The status code, end-to-end response headers, and body are copied from the downstream
/// response, making this ideal for gateway and proxy scenarios where an operation forwards a call to
/// another service and returns the raw response.
/// </summary>
/// <remarks>
/// <para>
/// The result owns the <see cref="HttpResponseMessage" /> and disposes it once the body has been written, or the
/// write has failed or been abandoned because the caller went away. The body is copied as it is read, so a response
/// received at its headers (<see cref="HttpCompletionOption.ResponseHeadersRead"/>) streams through unbuffered:
/// <c>BlueprintHttpClient.ForwardAsync</c> returns one so when its declared result is a <see cref="ProxyResult"/>.
/// </para>
/// <para>
/// Hop-by-hop headers (<c>Connection</c>, <c>Keep-Alive</c>, <c>Transfer-Encoding</c>, <c>Upgrade</c>,
/// <c>Proxy-*</c>, <c>TE</c> and <c>Trailer</c>, and any header the downstream <c>Connection</c> header names) describe
/// the downstream connection, not the response, so they are not forwarded, whether they arrive as response or content
/// headers. Content headers such as <c>Content-Type</c> and <c>Content-Length</c> are copied from
/// <see cref="HttpResponseMessage.Content"/> separately to ensure the ASP.NET Core response is well-formed.
/// </para>
/// <para>
/// A result Blueprint's HTTP host serves never goes undisposed: the host also disposes the response with the request, so
/// an <c>IHttpResultWriter</c> or an operation result executor that answers in this result's place, or fails on it,
/// without executing it does not leave the downstream connection open.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task&lt;ProxyResult&gt; HandleAsync(LoadAnalyticsQuery operation, HttpClient httpClient, CancellationToken cancellationToken)
/// {
///     var response = await httpClient.GetAsync(
///         $"/cubejs-api/v1/load?query={operation.Query}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
///     return new ProxyResult(response);
/// }
/// </code>
/// </example>
public class ProxyResult : HttpResult
{
    private readonly HttpResponseMessage _downstreamResponse;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyResult" /> class that will proxy the given
    /// downstream <see cref="HttpResponseMessage" /> to the client, and dispose it once written.
    /// </summary>
    /// <param name="downstreamResponse">The downstream HTTP response to proxy, which this result now owns.</param>
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
        try
        {
            // Set status code and any manually-added headers from the base class
            await base.ExecuteAsync(context).ConfigureAwait(false);

            var httpContext = context.HttpContext;

            if (httpContext is null)
            {
                return;
            }

            var response = httpContext.Response;

            // The headers the downstream Connection header names are hop-by-hop too (RFC 9110 7.6.1).
            var connectionNominated = this._downstreamResponse.Headers.Connection.Count == 0
                ? null
                : new HashSet<string>(this._downstreamResponse.Headers.Connection, StringComparer.OrdinalIgnoreCase);

            // Forward end-to-end response headers (e.g. Cache-Control, ETag, X-* custom headers)
            foreach (var (key, values) in this._downstreamResponse.Headers)
            {
                if (!IsHopByHop(key, connectionNominated))
                {
                    response.Headers[key] = new StringValues([.. values]);
                }
            }

            // Forward content headers (Content-Type, Content-Length, Content-Encoding, etc.)
            if (this._downstreamResponse.Content is not null)
            {
                foreach (var (key, values) in this._downstreamResponse.Content.Headers)
                {
                    if (!IsHopByHop(key, connectionNominated))
                    {
                        response.Headers[key] = new StringValues([.. values]);
                    }
                }

                // Sent now, so the caller has the headers while the body is still arriving from downstream.
                await response.StartAsync(httpContext.RequestAborted).ConfigureAwait(false);

                await this._downstreamResponse.Content.CopyToAsync(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
            }
        }
        finally
        {
            this._downstreamResponse.Dispose();
        }
    }

    private static bool IsHopByHop(string headerName, HashSet<string>? connectionNominated)
    {
        return connectionNominated?.Contains(headerName) == true
               || headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
               || headerName.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);
    }
}
