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
    /// Adds a consumer's outbox listener to <paramref name="options"/> after the session listener that dispatches and
    /// publishes domain events, adding that one first if it is not there yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the extension point for the listener that, during <c>SaveChangesAsync</c>, writes the messages sent so far in
    /// the current Rebus transaction into the session being saved, so that they commit with the data: the Marten half of
    /// the outbox whose Rebus half is an <c>IRebusTransactionEnlistment</c>. A message sent after the save — by a generated
    /// pipeline's message outbox commit, audit sink or background-task flush, or by a received message's later commit
    /// callback — is not written, and the <c>IRebusTransactionEnlistment</c> documentation says what the outbox must do
    /// with one. Marten runs listeners in
    /// <see cref="StoreOptions.Listeners"/> order, and <see cref="DomainEventDispatchingSessionListener"/> publishes the
    /// domain events during its own <c>BeforeSaveChangesAsync</c>, so an outbox listener must run after it: one that ran
    /// first would write none of them.
    /// </para>
    /// <para>
    /// Adding the listener to <see cref="StoreOptions.Listeners"/> directly gets that order wrong wherever the options
    /// are configured before the <c>ConfigureMarten</c> callback of <see cref="AddBlueprintMarten"/> runs — in the
    /// <c>AddMarten(options =&gt; ...)</c> callback, for one — because that callback appends the dispatching listener
    /// after it. This method adds the dispatching listener first, which that callback then leaves alone, and moves a
    /// listener that is already in the list to after it.
    /// </para>
    /// <para>
    /// It is idempotent: each listener ends up in the list once.
    /// </para>
    /// <para>
    /// Marten 6 runs no listener for a session with nothing to save, so on its own a drain would never write the messages
    /// of an operation that sent but wrote nothing. <see cref="MartenUnitOfWork"/> closes that gap for a listener added
    /// here: when it saves a session with nothing to save and one of the session's listeners was added here, it queues a
    /// statement that does nothing, so that Marten runs the listeners and saves what the drain writes. That costs one
    /// round trip for such a save, and needs the database even when nothing was sent. A drain that is also an
    /// <see cref="IOutboxDrainListener"/> avoids both: it is run that way only when it says it has messages to write, so a
    /// save that has nothing to write, and sends nothing, touches no database, as legacy's did. A drain that was never passed to this method — one added only to a session's own
    /// listeners, as legacy's per-session outbox listener was, or only to <see cref="StoreOptions.Listeners"/> — gets no
    /// such guarantee, and must make sure the session has something to save itself.
    /// </para>
    /// </remarks>
    /// <param name="options">The store options to add the listener to.</param>
    /// <param name="listener">The consumer's outbox listener.</param>
    /// <returns>The same <paramref name="options"/>, for chaining.</returns>
    public static StoreOptions AddBlueprintOutboxListener(this StoreOptions options, IDocumentSessionListener listener)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(listener);

        OutboxListenerRegistry.Register(listener);

        options.AddBlueprintSessionListeners();

        // Appended, so after the dispatching listener wherever that is; removed first, so it is there once, and so one
        // added before the dispatching listener moves after it.
        while (options.Listeners.Remove(listener))
        {
        }

        options.Listeners.Add(listener);

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
