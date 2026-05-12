using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

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
    /// Creates a new <see cref="System.Net.Http.HttpClient"/> that sends requests to the in-memory test server.
    /// This is an alias for the <see cref="Client"/> property that matches the S365 <c>CreateClient()</c>
    /// convention. Each call returns the same underlying client instance.
    /// </summary>
    /// <returns>An HTTP client connected to the test server.</returns>
    public System.Net.Http.HttpClient CreateClient()
    {
        return this.Client;
    }

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
    /// Adds a test authentication scheme that automatically authenticates every request with the given claims.
    /// This enables testing of operations marked with <c>[MustBeAuthenticated]</c> without
    /// requiring a real identity provider. Call this before <see cref="BuildAsync"/>.
    /// </summary>
    /// <param name="configureClaims">
    /// A callback that populates the claims for the test user identity. For example:
    /// <c>claims => { claims.Add(new Claim("sub", "test-user")); claims.Add(new Claim("role", "Admin")); }</c>
    /// </param>
    /// <param name="authenticationType">
    /// The authentication type name. Defaults to <c>"TestScheme"</c>. Must be non-null for
    /// <see cref="ClaimsIdentity.IsAuthenticated"/> to return <c>true</c>.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintHttpTestHostBuilder<TApp, TExec> WithTestAuthentication(
        Action<IList<Claim>> configureClaims,
        string authenticationType = "TestScheme")
    {
        ArgumentNullException.ThrowIfNull(configureClaims);

        var claims = new List<Claim>();
        configureClaims(claims);
        var identity = new ClaimsIdentity(claims, authenticationType);
        var principal = new ClaimsPrincipal(identity);

        this._serviceConfigurations.Add(services =>
        {
            services.AddAuthentication("TestScheme")
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("TestScheme", _ => { });
            services.AddSingleton(new TestAuthenticationPrincipal(principal));
        });

        this._appConfigurations.Insert(0, app =>
        {
            app.UseAuthentication();
        });

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

/// <summary>
/// Holds the pre-configured <see cref="ClaimsPrincipal"/> for the test authentication handler.
/// Registered as a singleton in DI by <see cref="BlueprintHttpTestHostBuilder{TApp,TExec}.WithTestAuthentication"/>.
/// </summary>
internal sealed class TestAuthenticationPrincipal
{
    /// <summary>Gets the pre-configured claims principal for test authentication.</summary>
    public ClaimsPrincipal Principal { get; }

    /// <summary>Initialises a new instance of the <see cref="TestAuthenticationPrincipal"/> class.</summary>
    /// <param name="principal">The claims principal to use for all authenticated requests.</param>
    public TestAuthenticationPrincipal(ClaimsPrincipal principal)
    {
        this.Principal = principal;
    }
}

/// <summary>
/// A minimal authentication handler that always succeeds with the pre-configured test principal.
/// Used by <see cref="BlueprintHttpTestHostBuilder{TApp,TExec}.WithTestAuthentication"/> to bypass
/// real identity providers in integration tests.
/// </summary>
internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly TestAuthenticationPrincipal _principal;

    /// <summary>Initialises a new instance of the <see cref="TestAuthenticationHandler"/> class.</summary>
    /// <param name="options">The authentication scheme options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    /// <param name="principal">The pre-configured test principal.</param>
    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        TestAuthenticationPrincipal principal)
        : base(options, logger, encoder)
    {
        this._principal = principal;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ticket = new AuthenticationTicket(this._principal.Principal, this.Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
