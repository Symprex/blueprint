using JetBrains.Annotations;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Marten;

/// <summary>
/// Extension methods for configuring Blueprint's Marten integration in the DI container.
/// </summary>
[PublicAPI]
public static class MartenServiceCollectionExtensions
{
    /// <summary>
    /// Registers Blueprint's Marten integration, including the <see cref="MartenUnitOfWork"/>
    /// implementation of <see cref="Persistence.IUnitOfWork"/> and the
    /// <see cref="DomainEventDispatchingSessionListener"/> for in-process domain event handling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method assumes Marten itself has already been registered via
    /// <c>services.AddMarten()</c>. It registers the Blueprint-specific wrappers
    /// that bridge Marten sessions into the generated pipeline's persistence and
    /// domain event stages.
    /// </para>
    /// <para>
    /// The <see cref="DomainEventDispatchingSessionListener"/> is added as a
    /// Marten session listener that dispatches <see cref="IDomainEvent"/> instances
    /// from entities implementing <see cref="IHaveDomainEvents"/> before
    /// <c>SaveChangesAsync</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBlueprintMarten(this IServiceCollection services)
    {
        services.AddScoped<Persistence.IUnitOfWork, MartenUnitOfWork>();

        services.ConfigureMarten(options =>
        {
            options.Listeners.Add(new DomainEventDispatchingSessionListener());
            options.Listeners.Add(new OutboxPublishingSessionListener());
        });

        return services;
    }
}
