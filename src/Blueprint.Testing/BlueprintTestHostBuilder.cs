using Blueprint.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.Testing;

/// <summary>
/// Builds a <see cref="BlueprintTestHost{TApp,TExec}"/> with configurable services,
/// authentication context, and optional test doubles. This is the primary factory
/// for creating test hosts in consumer test projects.
/// </summary>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> subclass discovered by <c>[BlueprintAppScan]</c>.</typeparam>
/// <typeparam name="TExec">The source-generated executor type.</typeparam>
public sealed class BlueprintTestHostBuilder<TApp, TExec>
    where TApp : BlueprintApp
    where TExec : BlueprintAppExecutor
{
    private readonly List<Action<IServiceCollection>> _serviceConfigurations = new();

    /// <summary>
    /// Creates a new builder for building a <see cref="BlueprintTestHost{TApp,TExec}"/>.
    /// </summary>
    /// <returns>A new builder instance.</returns>
    public static BlueprintTestHostBuilder<TApp, TExec> Create()
    {
        return new();
    }

    /// <summary>
    /// Configures additional services on the DI container. Can be called multiple times;
    /// configurations are applied in order.
    /// </summary>
    /// <param name="configure">The service configuration delegate.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintTestHostBuilder<TApp, TExec> ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        this._serviceConfigurations.Add(configure);
        return this;
    }

    /// <summary>
    /// Builds the <see cref="BlueprintTestHost{TApp,TExec}"/> with all configured services.
    /// The host is ready to execute operations immediately after construction.
    /// </summary>
    /// <returns>A fully configured test host.</returns>
    public BlueprintTestHost<TApp, TExec> Build()
    {
        var services = new ServiceCollection();

        // Register null logging by default — tests shouldn't write to console unless explicitly configured.
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Register the Blueprint app and executor via the standard registration path.
        services.AddBlueprintApi<TApp, TExec>();

        // Apply user-supplied configurations (test doubles, mocks, etc.)
        foreach (var configure in this._serviceConfigurations)
        {
            configure(services);
        }

        var rootProvider = services.BuildServiceProvider();

        var app = rootProvider.GetRequiredService<TApp>();
        var executor = rootProvider.GetRequiredService<TExec>();

        return new BlueprintTestHost<TApp, TExec>(rootProvider, app, executor);
    }
}
