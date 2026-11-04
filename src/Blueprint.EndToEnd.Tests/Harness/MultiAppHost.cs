using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Blueprint.EndToEnd.Tests.Harness;

/// <summary>
/// Starts several ASP.NET Core apps in the test process, each through its real <c>Startup</c> on real
/// Kestrel at an ephemeral loopback port, all trusting one <see cref="TestJwtIssuer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Apps talk to each other over real sockets with real <see cref="System.Net.Http.HttpClient"/>s, so auth
/// headers, streaming, hop-by-hop headers and connection behaviour are what production sees. That is the
/// difference from the two-host <c>TestServer</c> pattern, which hands one app the other's in-memory handler.
/// </para>
/// <para>
/// Start apps in dependency order: an app's address is only known once it is listening, and a later app is
/// pointed at it through <see cref="EndToEndAppOptions.Settings"/>. Disposal stops them in reverse order.
/// </para>
/// </remarks>
public sealed class MultiAppHost : IAsyncDisposable
{
    private readonly List<RunningApp> _apps = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiAppHost"/> class.
    /// </summary>
    /// <param name="issuer">The issuer every app started by this host trusts.</param>
    public MultiAppHost(TestJwtIssuer issuer)
    {
        this.Issuer = issuer;
    }

    /// <summary>The issuer every app started by this host trusts.</summary>
    public TestJwtIssuer Issuer { get; }

    /// <summary>The apps started so far, in start order.</summary>
    public IReadOnlyList<RunningApp> Apps => this._apps;

    /// <summary>Gets a started app by the name it was started under.</summary>
    /// <param name="name">The app's name.</param>
    public RunningApp this[string name] =>
        this._apps.SingleOrDefault(a => a.Name == name)
        ?? throw new KeyNotFoundException(
            $"No app named '{name}' has been started. Started: [{string.Join(", ", this._apps.Select(static a => a.Name))}].");

    /// <summary>
    /// Starts an app through <typeparamref name="TStartup"/> on a fresh loopback port and waits until it is
    /// listening.
    /// </summary>
    /// <typeparam name="TStartup">The app's own <c>Startup</c> class, used unmodified.</typeparam>
    /// <param name="name">A name unique within this host.</param>
    /// <param name="configure">Adjusts how the app is started; see <see cref="EndToEndAppOptions"/>.</param>
    /// <returns>The running app, with the address Kestrel bound.</returns>
    public async Task<RunningApp> StartAsync<TStartup>(string name, Action<EndToEndAppOptions>? configure = null)
        where TStartup : class
    {
        if (this._apps.Any(a => a.Name == name))
        {
            throw new InvalidOperationException($"An app named '{name}' has already been started.");
        }

        var options = new EndToEndAppOptions();
        configure?.Invoke(options);

        // The same builder the sample Program classes use, so defaults such as Development's scope validation
        // apply here too. Only the listening address, the issuer and the options' overrides differ.
        var host = Host.CreateDefaultBuilder()
            .UseEnvironment(options.Environment)
            .ConfigureLogging(static logging => logging.ClearProviders())
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(options.Settings))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseStartup<TStartup>();

                // Port 0 lets the OS pick a free port, so parallel runs and other local servers never collide.
                web.UseUrls("http://127.0.0.1:0");
                web.ConfigureServices(services =>
                {
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(this.Issuer.ConfigureBearer);
                    services.AddSingleton<IStartupFilter, AuthenticateFirstStartupFilter>();

                    if (options.ForwardCallerCredential)
                    {
                        services.AddHttpContextAccessor();
                        services.AddTransient<ForwardCallerAuthorizationHandler>();
                        services.ConfigureHttpClientDefaults(
                            static client => client.AddHttpMessageHandler<ForwardCallerAuthorizationHandler>());
                    }

                    options.ConfigureServices?.Invoke(services);
                });
            })
            .Build();

        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }

        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var app = new RunningApp(name, new Uri(address.EndsWith('/') ? address : address + "/"), host);
        this._apps.Add(app);

        return app;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Every app is stopped even if an earlier one fails to, so no listener outlives the fixture.
        List<Exception>? failures = null;

        for (var i = this._apps.Count - 1; i >= 0; i--)
        {
            try
            {
                await this._apps[i].DisposeAsync();
            }
            catch (Exception e)
            {
                (failures ??= []).Add(e);
            }
        }

        this._apps.Clear();

        if (failures is not null)
        {
            throw new AggregateException("One or more apps failed to stop cleanly.", failures);
        }
    }

    /// <summary>
    /// Puts <c>UseAuthentication()</c> ahead of the app's own pipeline, so <c>HttpContext.User</c> is the
    /// bearer token's principal by the time Blueprint's generated authorisation checks run.
    /// </summary>
    /// <remarks>
    /// A startup filter, rather than an edit to the sample's <c>Configure</c>, keeps that method running
    /// exactly as it does in production. The samples leave authentication to the consumer, as their own
    /// comments say.
    /// </remarks>
    private sealed class AuthenticateFirstStartupFilter : IStartupFilter
    {
        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseAuthentication();
                next(app);
            };
        }
    }
}
