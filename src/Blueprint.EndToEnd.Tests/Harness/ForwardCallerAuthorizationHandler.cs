using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace Blueprint.EndToEnd.Tests.Harness;

/// <summary>
/// Copies the inbound caller's <c>Authorization</c> header onto an outbound request that has none. Installed
/// only when <see cref="EndToEndAppOptions.ForwardCallerCredential"/> is set.
/// </summary>
/// <param name="httpContextAccessor">Reads the request the app is currently serving.</param>
internal sealed class ForwardCallerAuthorizationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A header the app set itself wins: the harness only fills a gap, it never overrides a credential choice.
        if (request.Headers.Authorization is null
            && httpContextAccessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString() is { Length: > 0 } inbound
            && AuthenticationHeaderValue.TryParse(inbound, out var credential))
        {
            request.Headers.Authorization = credential;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
