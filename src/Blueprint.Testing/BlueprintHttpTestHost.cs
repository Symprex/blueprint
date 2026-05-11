using Blueprint.Model;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.TestHost;

namespace Blueprint.Testing;

/// <summary>
/// Provides a full HTTP test host for end-to-end testing of Blueprint operations through
/// the ASP.NET Core HTTP pipeline. Unlike <see cref="BlueprintTestHost{TApp,TExec}"/> which
/// executes operations in-process, this host starts a real <see cref="TestServer"/> and
/// provides an <see cref="System.Net.Http.HttpClient"/> for sending HTTP requests.
/// <para>
/// Use this when tests need to exercise the full HTTP stack including routing, content
/// negotiation, middleware, authentication, model binding from route/query/body, and
/// serialization — mirroring production behaviour exactly.
/// </para>
/// </summary>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> subclass discovered by <c>[BlueprintAppScan]</c>.</typeparam>
/// <typeparam name="TExec">The source-generated executor type.</typeparam>
public sealed class BlueprintHttpTestHost<TApp, TExec> : IDisposable, IAsyncDisposable
    where TApp : BlueprintApp, new()
    where TExec : BlueprintAppExecutor
{
    private readonly IHost _host;
    private bool _disposed;

    /// <summary>
    /// Gets the <see cref="System.Net.Http.HttpClient"/> that sends requests to the in-memory test server.
    /// All requests are handled by the full Blueprint HTTP pipeline (routing → pipeline → result execution).
    /// </summary>
    public System.Net.Http.HttpClient Client { get; }

    /// <summary>
    /// Gets the root service provider of the test host. Useful for resolving services
    /// registered alongside Blueprint (e.g. to seed test data or verify side effects).
    /// </summary>
    public IServiceProvider Services => this._host.Services;

    /// <summary>
    /// Gets the app model describing all registered operations.
    /// </summary>
    public TApp App => this._host.Services.GetRequiredService<TApp>();

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintHttpTestHost{TApp,TExec}"/> class.
    /// </summary>
    /// <param name="host">The running host instance.</param>
    internal BlueprintHttpTestHost(IHost host)
    {
        this._host = host;
        this.Client = host.GetTestClient();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!this._disposed)
        {
            this.Client.Dispose();
            this._host.Dispose();
            this._disposed = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!this._disposed)
        {
            this.Client.Dispose();

            if (this._host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                this._host.Dispose();
            }

            this._disposed = true;
        }
    }
}

/// <summary>
/// Builds a <see cref="BlueprintHttpTestHost{TApp,TExec}"/> with configurable services,
/// middleware, and Blueprint HTTP options. The built host runs the full ASP.NET Core
/// pipeline in-memory via <see cref="TestServer"/>.
/// </summary>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> subclass.</typeparam>
/// <typeparam name="TExec">The source-generated executor type.</typeparam>
public sealed class BlueprintHttpTestHostBuilder<TApp, TExec>
    where TApp : BlueprintApp, new()
    where TExec : BlueprintAppExecutor
{
    private readonly List<Action<IServiceCollection>> _serviceConfigurations = new();
    private readonly List<Action<IApplicationBuilder>> _appConfigurations = new();
    private Action<BlueprintHttpOptions>? _httpOptionsAction;
    private string _basePath = "/api";

    /// <summary>
    /// Creates a new builder.
    /// </summary>
    /// <returns>A new builder instance.</returns>
    public static BlueprintHttpTestHostBuilder<TApp, TExec> Create()
    {
        return new();
    }

    /// <summary>
    /// Configures additional services on the DI container. Can be called multiple times.
    /// </summary>
    /// <param name="configure">The service configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintHttpTestHostBuilder<TApp, TExec> ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._serviceConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Configures additional middleware on the application pipeline. This is applied
    /// BEFORE Blueprint's UseBlueprint call, so it can add authentication/authorization
    /// middleware, CORS, etc.
    /// </summary>
    /// <param name="configure">The application builder configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintHttpTestHostBuilder<TApp, TExec> ConfigureApp(Action<IApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._appConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Configures Blueprint HTTP options (public host, base path overrides, etc.).
    /// </summary>
    /// <param name="configure">The options configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintHttpTestHostBuilder<TApp, TExec> ConfigureHttpOptions(Action<BlueprintHttpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._httpOptionsAction = configure;
        return this;
    }

    /// <summary>
    /// Sets the base path prefix under which Blueprint endpoints are mapped (default: "/api").
    /// </summary>
    /// <param name="basePath">The base path prefix.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintHttpTestHostBuilder<TApp, TExec> WithBasePath(string basePath)
    {
        this._basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        return this;
    }

    /// <summary>
    /// Builds and starts the HTTP test host. The host is immediately ready to accept requests.
    /// </summary>
    /// <returns>A fully configured and running HTTP test host.</returns>
    public async Task<BlueprintHttpTestHost<TApp, TExec>> BuildAsync()
    {
        var basePath = this._basePath;
        var serviceConfigurations = this._serviceConfigurations;
        var appConfigurations = this._appConfigurations;
        var httpOptionsAction = this._httpOptionsAction;

        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddBlueprintApi<TApp, TExec>();
                    services.AddRouting();

                    if (httpOptionsAction is not null)
                    {
                        services.Configure(httpOptionsAction);
                    }

                    foreach (var configure in serviceConfigurations)
                    {
                        configure(services);
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();

                    foreach (var configure in appConfigurations)
                    {
                        configure(app);
                    }

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapBlueprintApi<TApp, TExec>(basePath);
                    });
                });
            });

        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);

        return new BlueprintHttpTestHost<TApp, TExec>(host);
    }
}
