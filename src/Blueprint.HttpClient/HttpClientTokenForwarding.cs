using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.HttpClient;

/// <summary>
/// Helpers for forwarding authentication tokens from an incoming HTTP request to outbound
/// <see cref="BlueprintHttpClient{TApp}"/> calls. This is the standard pattern for gateway
/// services that receive a JWT from an external caller and need to forward it to backend
/// Blueprint microservices.
/// </summary>
public static class HttpClientTokenForwarding
{
    /// <summary>
    /// Copies the <c>Authorization</c> header from the current HTTP request to the outbound
    /// <see cref="System.Net.Http.HttpClient"/>. If no request is in scope or the header is
    /// absent, the client is left unmodified. Optionally also copies the <c>User-Agent</c>
    /// header for request tracing.
    /// </summary>
    /// <param name="serviceProvider">The scoped service provider (from the DI-based client factory).</param>
    /// <param name="httpClient">The outbound HTTP client being configured.</param>
    /// <param name="forwardUserAgent">
    /// When <c>true</c>, also copies the incoming <c>User-Agent</c> header. Default is <c>true</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Requires <see cref="IHttpContextAccessor"/> to be registered in DI (typically via
    /// <c>services.AddHttpContextAccessor()</c>). If the accessor is not registered, this method
    /// is a no-op.
    /// </para>
    /// <para>
    /// Typical usage in gateway registration:
    /// <code>
    /// services.AddBlueprintHttpClient&lt;BackendApp&gt;((sp, client) =>
    /// {
    ///     client.BaseAddress = new Uri("https://backend-service/api/");
    ///     HttpClientTokenForwarding.ForwardAuthorizationHeader(sp, client);
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static void ForwardAuthorizationHeader(
        IServiceProvider serviceProvider,
        System.Net.Http.HttpClient httpClient,
        bool forwardUserAgent = true)
    {
        var accessor = serviceProvider.GetService<IHttpContextAccessor>();
        var httpContext = accessor?.HttpContext;

        if (httpContext is null)
        {
            return;
        }

        var authHeader = httpContext.Request.Headers.Authorization;
        if (authHeader.Count > 0)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader.ToString());
        }

        if (forwardUserAgent)
        {
            var userAgent = httpContext.Request.Headers.UserAgent;
            if (userAgent.Count > 0)
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent.ToString());
            }
        }
    }
}
