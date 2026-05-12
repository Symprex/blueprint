using Blueprint.Tenancy;
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

    /// <summary>
    /// Registers Blueprint's Marten integration with tenant-aware document sessions.
    /// When an <see cref="ITenantAccessor"/> is available and has a current tenant,
    /// sessions are opened with that tenant ID for Marten's conjoined multi-tenancy
    /// (<c>TenancyStyle.Conjoined</c>). When no tenant is active, a default session is opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This overload replaces the default <c>IDocumentSession</c> registration with a
    /// tenant-scoped factory that reads the current tenant from <see cref="ITenantAccessor.Current"/>.
    /// Callers should ensure <see cref="ITenantAccessor"/> is registered (the generated pipeline's
    /// <c>TenantModule</c> sets <see cref="ITenantAccessor.Current"/> from JWT claims before
    /// the handler executes).
    /// </para>
    /// <para>
    /// For Marten multi-tenancy to work, configure document types with
    /// <c>TenancyStyle.Conjoined</c> in the <c>AddMarten()</c> call.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBlueprintMartenWithTenancy(this IServiceCollection services)
    {
        // Register the tenant-aware session factory.
        services.AddScoped<IDocumentSession>(sp =>
        {
            var store = sp.GetRequiredService<IDocumentStore>();
            var tenantAccessor = sp.GetService<ITenantAccessor>();
            var tenantId = tenantAccessor?.Current;

            return !string.IsNullOrEmpty(tenantId)
                ? store.IdentitySession(tenantId)
                : store.IdentitySession();
        });

        services.AddScoped<Persistence.IUnitOfWork, MartenUnitOfWork>();

        services.ConfigureMarten(options =>
        {
            options.Listeners.Add(new DomainEventDispatchingSessionListener());
            options.Listeners.Add(new OutboxPublishingSessionListener());
        });

        return services;
    }
}
