using Blueprint.Marten;
using Blueprint.Marten.Rebus;
using JetBrains.Annotations;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring the Blueprint Marten + Rebus bridge.
/// </summary>
[PublicAPI]
public static class MartenRebusServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Rebus-backed <see cref="IDomainEventPublisher"/> so that domain events
    /// raised by entities implementing <see cref="IHaveDomainEvents"/> are published to the
    /// Rebus bus during Marten's <c>SaveChangesAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this <b>after</b> both <c>AddBlueprintMarten()</c> (or <c>AddBlueprintMartenWithTenancy()</c>)
    /// and <c>AddBlueprintRebus()</c> to wire the domain event flow:
    /// </para>
    /// <code>
    /// services.AddBlueprintMartenWithTenancy();
    /// services.AddBlueprintRebus((sp, cfg) =&gt; cfg.Transport(t =&gt; ...));
    /// services.AddBlueprintMartenRebus(); // bridges the two
    /// </code>
    /// <para>
    /// The <see cref="RebusDomainEventPublisher"/> resolves <c>IBus</c> from DI and publishes
    /// events via <c>bus.Publish()</c>. When the generated pipeline's <c>RebusTransactionModule</c>
    /// is active, these publishes are captured by the ambient <c>RebusTransactionScope</c> and
    /// only sent after successful completion.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBlueprintMartenRebus(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventPublisher, RebusDomainEventPublisher>();

        return services;
    }
}
