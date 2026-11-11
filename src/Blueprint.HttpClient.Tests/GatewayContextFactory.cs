using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Gateway;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Creates the <see cref="ApiOperationContext"/> the sample gateway serves one of its operations with, as the HTTP host does,
/// for a caller that sent a given <c>Authorization</c> header.
/// </summary>
internal sealed class GatewayContextFactory : IAsyncDisposable
{
    /// <summary>The token the sample gateway's <see cref="ServiceTokenCredentialStrategy"/> is configured with.</summary>
    public const string ServiceToken = "service-token";

    private readonly ServiceProvider _services;
    private readonly GatewayBlueprintApp _app;

    /// <summary>
    /// Initialises a new instance of the <see cref="GatewayContextFactory"/> class, with the sample gateway's services.
    /// </summary>
    public GatewayContextFactory()
    {
        this._services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [Startup.ServiceTokenKey] = ServiceToken })
                .Build())
            .AddSingleton<ServiceTokenCredentialStrategy>()
            .AddBlueprintApi<GatewayBlueprintApp, GatewayBlueprintAppExecutor>()
            .BuildServiceProvider();
        this._app = this._services.GetRequiredService<GatewayBlueprintApp>();
    }

    /// <summary>
    /// Creates the context <typeparamref name="TOperation"/> is served with.
    /// </summary>
    /// <typeparam name="TOperation">The gateway operation.</typeparam>
    /// <param name="callerAuthorization">The caller's <c>Authorization</c> header, or <see langword="null"/> for none.</param>
    /// <returns>The context.</returns>
    public ApiOperationContext For<TOperation>(string? callerAuthorization = null)
    {
        var httpContext = new DefaultHttpContext { RequestServices = this._services };

        if (callerAuthorization is not null)
        {
            httpContext.Request.Headers.Authorization = callerAuthorization;
        }

        return new ApiOperationContext(this._services, this._app, this._app.FindOperation(typeof(TOperation)), CancellationToken.None)
        {
            HttpContext = httpContext,
        };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return this._services.DisposeAsync();
    }
}
