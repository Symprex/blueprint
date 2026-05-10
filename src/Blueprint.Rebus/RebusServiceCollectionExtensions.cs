using Blueprint.Model;
using Blueprint.Rebus;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Pipeline;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for integrating Rebus message handling with Blueprint's generated pipeline.
/// </summary>
public static class RebusServiceCollectionExtensions
{
    /// <summary>
    /// Configures the subscribed Rebus message types and applies Rebus-specific conventions to their
    /// <see cref="ApiOperationDescriptor"/> entries (anonymous access, not exposed, no return value required).
    /// </summary>
    /// <param name="services">The services collection.</param>
    /// <param name="configure">A callback to subscribe message types via <see cref="BlueprintRebusConfiguration.Subscribe{T}"/>.</param>
    /// <returns>The services collection for chaining.</returns>
    public static IServiceCollection AddBlueprintRebusConsumer(
        this IServiceCollection services,
        Action<BlueprintRebusConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var config = new BlueprintRebusConfiguration();
        configure(config);

        services.AddSingleton(config);

        return services;
    }

    /// <summary>
    /// Adds Rebus, configured with Blueprint integration, to the given services collection. Creates
    /// the <see cref="IBus"/> singleton using <see cref="BlueprintApiActivator"/> as the handler activator.
    /// </summary>
    /// <param name="services">The services to register with.</param>
    /// <param name="configure">The action used to configure Rebus itself.</param>
    /// <returns>The passed in services collection.</returns>
    /// <exception cref="InvalidOperationException">If a Rebus Bus has already been registered.</exception>
    public static IServiceCollection AddBlueprintRebus(
        this IServiceCollection services,
        Func<IServiceProvider, RebusConfigurer, RebusConfigurer> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var existingBusRegistration = services.SingleOrDefault(
            static descriptor => descriptor.ServiceType == typeof(IBus));

        if (existingBusRegistration != null)
        {
            // If a FakeBus is already registered (test environments), skip Rebus registration
            if (!existingBusRegistration.ImplementationType?.Name.EndsWith(
                    "FakeBus", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                throw new InvalidOperationException(
                    "Rebus has already been configured in this service collection.");
            }

            return services;
        }

        services.AddSingleton(s =>
        {
            var blueprintActivator = new BlueprintApiActivator(s);
            var busStarter = configure(s, Configure.With(blueprintActivator)).Create();

            return busStarter;
        });

        services.AddSingleton(static s => s.GetRequiredService<IBusStarter>().Bus);
        services.AddSingleton(static s => s.GetRequiredService<IBus>().Advanced.SyncBus);
        services.AddTransient(static _ =>
            MessageContext.Current ?? throw new InvalidOperationException(
                "Attempted to resolve IMessageContext outside of a Rebus handler. " +
                "Rebus' message context is only available to code executing inside a Rebus handler."));

        return services;
    }

    /// <summary>
    /// Activates the Rebus engine, allowing it to start sending and receiving messages.
    /// </summary>
    /// <param name="provider">The service provider configured for Rebus.</param>
    /// <returns>The service provider for chaining.</returns>
    public static IServiceProvider StartRebus(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        provider.GetRequiredService<IBusStarter>().Start();

        return provider;
    }
}
