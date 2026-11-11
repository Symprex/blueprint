using System.Collections.Concurrent;
using Microsoft.Net.Http.Headers;

namespace Blueprint.EndToEnd.Tests.Harness;

/// <summary>
/// Records the <c>Authorization</c> header of every request an app receives, as the app received it off the socket, so a
/// test can assert what credential a caller, such as a gateway, actually sent.
/// </summary>
public sealed class InboundRequestLog
{
    private readonly ConcurrentQueue<InboundRequest> _requests = new();

    /// <summary>
    /// Gets the requests received so far, oldest first.
    /// </summary>
    public IReadOnlyList<InboundRequest> Requests => [.. this._requests];

    /// <summary>
    /// Records every request the app receives, ahead of the app's own pipeline.
    /// </summary>
    /// <param name="services">The app's services.</param>
    public void InstallInto(IServiceCollection services)
    {
        services.AddSingleton<IStartupFilter>(new RecordingStartupFilter(this));
    }

    /// <summary>
    /// Gets the <c>Authorization</c> header of the latest request for <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The request path, such as <c>/api/templates/1</c>.</param>
    /// <returns>The header as received, or <see langword="null"/> when that request carried none.</returns>
    /// <exception cref="InvalidOperationException">No request for <paramref name="path"/> was received.</exception>
    public string? LatestAuthorizationFor(string path)
    {
        var request = this.Requests.LastOrDefault(r => string.Equals(r.Path, path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No request for '{path}' was received.");

        return request.Authorization;
    }

    /// <summary>
    /// One request the app received.
    /// </summary>
    /// <param name="Path">The request path.</param>
    /// <param name="Authorization">The <c>Authorization</c> header, or <see langword="null"/> when there was none.</param>
    public sealed record InboundRequest(string Path, string? Authorization);

    private sealed class RecordingStartupFilter(InboundRequestLog log) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    var authorization = context.Request.Headers[HeaderNames.Authorization].ToString();
                    log._requests.Enqueue(new InboundRequest(context.Request.Path.Value ?? string.Empty, authorization.Length == 0 ? null : authorization));

                    return nextMiddleware(context);
                });

                next(app);
            };
        }
    }
}
