using System.Net.Http.Headers;

namespace Blueprint.EndToEnd.Tests.Harness;

/// <summary>
/// One app started by a <see cref="MultiAppHost"/>, listening on its own Kestrel loopback port.
/// </summary>
public sealed class RunningApp : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _stopped;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunningApp"/> class around a host that is already listening.
    /// </summary>
    /// <param name="name">The name the app was started under.</param>
    /// <param name="baseAddress">The root address Kestrel bound, ending in a slash.</param>
    /// <param name="host">The started host, owned from here on.</param>
    internal RunningApp(string name, Uri baseAddress, IHost host)
    {
        this.Name = name;
        this.BaseAddress = baseAddress;
        this._host = host;
    }

    /// <summary>The name the app was started under, unique within its <see cref="MultiAppHost"/>.</summary>
    public string Name { get; }

    /// <summary>
    /// The root address Kestrel bound, such as <c>http://127.0.0.1:53817/</c>. It always ends in a slash, so
    /// a relative path such as <c>api/</c> resolves beneath it.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>The app's root service provider, for a test that must inspect or seed app state directly.</summary>
    public IServiceProvider Services => this._host.Services;

    /// <summary>
    /// Creates a client that talks to this app over a real socket, sending <paramref name="bearerToken"/>
    /// on every request when one is given. The caller owns and disposes the client.
    /// </summary>
    /// <param name="bearerToken">A token from the host's <see cref="TestJwtIssuer"/>, or <see langword="null"/> to call anonymously.</param>
    /// <returns>A client whose <see cref="System.Net.Http.HttpClient.BaseAddress"/> is <see cref="BaseAddress"/>.</returns>
    public System.Net.Http.HttpClient CreateClient(string? bearerToken = null)
    {
        var client = new System.Net.Http.HttpClient { BaseAddress = this.BaseAddress };

        if (bearerToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return client;
    }

    /// <summary>
    /// Stops the app and releases its port. Safe to call more than once; the host also calls it on disposal.
    /// </summary>
    /// <param name="cancellationToken">Cancels a graceful shutdown that is taking too long.</param>
    /// <returns>A task that completes once Kestrel has stopped listening.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (this._stopped)
        {
            return;
        }

        this._stopped = true;
        await this._host.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;

        try
        {
            await this.StopAsync();
        }
        finally
        {
            // A hosted service that throws on shutdown must not leave the service provider and listener alive.
            this._host.Dispose();
        }
    }
}
