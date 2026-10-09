using Blueprint;
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
    /// Configures the subscribed Rebus message types, applies Rebus-specific conventions to their
    /// <see cref="ApiOperationDescriptor"/> entries (a <see cref="RebusMessageFeature"/>, anonymous access, not exposed),
    /// and auto-registers all <c>IHandleMessages&lt;T&gt;</c> implementations found in the calling assembly
    /// as scoped services in DI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The conventions are an <see cref="IOperationDescriptorConvention"/>, which <c>AddBlueprintApi</c> applies as the app
    /// is made, whether this is called before or after it, so a subscribed message allows anonymous access before it first
    /// executes, as legacy Signature365's <c>RebusScannerConvention</c> made it at start-up (Signature365 <c>8b40ecc961</c>,
    /// <c>Symprex.Signature365.BlueprintRebus/RebusScannerConvention.cs:30-44</c>). A message is executed with no user, and
    /// an unlinked operation that does not allow anonymous access refuses one.
    /// </para>
    /// <para>
    /// An <c>IHandleMessages&lt;TBase&gt;</c> is also registered as the handler of every subscribed message type assignable
    /// to <c>TBase</c> — derived from a base class, or implementing an interface — so that it runs for such a message as it
    /// did in legacy, where every subscribed message was an operation given every scanned handler of a type it could be
    /// assigned to (Signature365 <c>8b40ecc961</c>,
    /// <c>Symprex.Signature365.BlueprintRebus/RebusHandleMessageOperationExecutorBuilderScanner.cs:50-75</c>). Only
    /// subscribed types are given base-type handlers, as only they were operations there. A handler of a type derived from
    /// the message's type is not registered for it: it cannot be given the message.
    /// </para>
    /// </remarks>
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
        services.AddSingleton<IOperationDescriptorConvention>(new RebusMessageDescriptorConvention(config));

        // Auto-register IHandleMessages<T> implementations from the calling assembly
        // so that plain Rebus handlers (not Blueprint operations) are resolved from DI.
        var callingAssembly = System.Reflection.Assembly.GetCallingAssembly();
        RegisterRebusHandlers(services, callingAssembly, config.SubscribedMessages);

        return services;
    }

    private static void RegisterRebusHandlers(
        IServiceCollection services,
        System.Reflection.Assembly assembly,
        IReadOnlyList<Type> subscribedMessages)
    {
        var handleMessagesOpenType = typeof(Rebus.Handlers.IHandleMessages<>);

        // Legacy kept one operation per message type, however often it was subscribed (ApiDataModel.RegisterOperation).
        var distinctSubscribedMessages = subscribedMessages.Distinct().ToList();

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            var handlerInterfaces = type.GetInterfaces()
                .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Rebus.Handlers.IHandleMessages<>));

            foreach (var handlerInterface in handlerInterfaces)
            {
                services.AddScoped(handlerInterface, type);

                // In the same pass, so that a subscribed message's handlers are registered, and so resolved and run, in the
                // order legacy's scanner gave them to its operation: by handler type, then by handled interface, whichever
                // of them is the message's own type. IHandleMessages<in TMessage> is contravariant, so the handler is an
                // IHandleMessages<TSubscribed> as registered.
                var handledType = handlerInterface.GetGenericArguments()[0];

                foreach (var subscribedMessage in distinctSubscribedMessages)
                {
                    if (subscribedMessage != handledType && handledType.IsAssignableFrom(subscribedMessage))
                    {
                        services.AddScoped(handleMessagesOpenType.MakeGenericType(subscribedMessage), type);
                    }
                }
            }
        }
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
