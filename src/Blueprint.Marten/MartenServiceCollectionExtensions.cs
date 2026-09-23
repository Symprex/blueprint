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
    /// Marten session listener that dispatches and publishes <see cref="IDomainEvent"/> instances
    /// from entities implementing <see cref="IHaveDomainEvents"/> before
    /// <c>SaveChangesAsync</c>.
    /// </para>
    /// <para>
    /// The listener is added through <c>ConfigureMarten</c>, which only a store configured by <c>AddMarten()</c>
    /// applies. A consumer that builds <see cref="StoreOptions"/> itself must call
    /// <see cref="AddBlueprintSessionListeners"/> on them as well.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBlueprintMarten(this IServiceCollection services)
    {
        services.AddScoped<Persistence.IUnitOfWork, MartenUnitOfWork>();

        services.ConfigureMarten(static options => options.AddBlueprintSessionListeners());

        return services;
    }

    /// <summary>
    /// Adds the session listener that dispatches and publishes the domain events of entities implementing
    /// <see cref="IHaveDomainEvents"/> to <paramref name="options"/>, unless it is already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how a consumer that builds its own <see cref="StoreOptions"/> — registering them as a singleton, or
    /// subclassing them, rather than configuring the store through <c>AddMarten()</c> — gets the listener that
    /// <see cref="AddBlueprintMarten"/> and <see cref="AddBlueprintMartenWithTenancy"/> would otherwise attach, since the
    /// <c>ConfigureMarten</c> callback those register is never applied to options Marten did not build.
    /// </para>
    /// <para>
    /// It is idempotent, so a consumer may call it and also go through <c>AddMarten()</c> with
    /// <see cref="AddBlueprintMarten"/> without an event being dispatched or published twice.
    /// </para>
    /// </remarks>
    /// <param name="options">The store options to add the listener to.</param>
    /// <returns>The same <paramref name="options"/>, for chaining.</returns>
    public static StoreOptions AddBlueprintSessionListeners(this StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Listeners.Any(static listener => listener is DomainEventDispatchingSessionListener))
        {
            options.Listeners.Add(new DomainEventDispatchingSessionListener());
        }

        return options;
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

        services.ConfigureMarten(static options => options.AddBlueprintSessionListeners());

        return services;
    }
}
