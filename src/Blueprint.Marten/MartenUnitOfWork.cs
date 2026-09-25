using Marten;

namespace Blueprint.Marten;

/// <summary>
/// Implements <see cref="Blueprint.Persistence.IUnitOfWork"/> by delegating to a Marten
/// <see cref="IDocumentSession"/>. Register as a scoped service so each operation
/// execution gets the same session instance that was injected into the handler.
/// </summary>
/// <remarks>
/// <para>
/// The generated pipeline calls <c>SaveChangesAsync</c> in the PostExecution stage
/// after successful handler execution. The session is disposed by the DI scope.
/// </para>
/// <para>
/// Constructed with the scope it was resolved from, as the DI container does, it makes that scope
/// <see cref="MartenScopeAccessor.Current"/> for the duration of the save, so the session's
/// <see cref="DomainEventDispatchingSessionListener"/> dispatches wherever the save is made from: a generated pipeline,
/// which also sets it, or the commit of a plain Rebus handler's message, which does not.
/// </para>
/// <para>
/// Marten 6 runs no session listener when a session has nothing to save: its <c>SaveChangesAsync</c> returns before the
/// first <c>BeforeSaveChangesAsync</c>. So when the session has nothing to save but one of its listeners is an outbox drain
/// added with <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>, the save first queues
/// <see cref="DrainTriggerSql"/>, which does nothing, so that the drain runs and what it writes is saved. That is the one
/// case in which this save opens a connection for a session with nothing to save.
/// </para>
/// </remarks>
public sealed class MartenUnitOfWork : Persistence.IUnitOfWork
{
    /// <summary>
    /// The statement queued to make Marten run the listeners of a session with nothing else to save: an empty block, so
    /// it returns no result set, as Marten expects of SQL queued with <see cref="IDocumentOperations.QueueSqlCommand"/>. A
    /// statement that returned one would be read as the result of the next operation in the batch.
    /// </summary>
    internal const string DrainTriggerSql = "DO $$BEGIN END$$";

    private readonly IDocumentSession _session;
    private readonly IServiceProvider? _services;

    /// <summary>
    /// Initialises the unit of work with the Marten session for the current scope. The save leaves
    /// <see cref="MartenScopeAccessor.Current"/> as it finds it.
    /// </summary>
    /// <param name="session">The scoped Marten document session.</param>
    public MartenUnitOfWork(IDocumentSession session)
    {
        this._session = session;
    }

    /// <summary>
    /// Initialises the unit of work with the Marten session for the current scope and that scope's service provider,
    /// which the save makes <see cref="MartenScopeAccessor.Current"/> for the session's listeners.
    /// </summary>
    /// <param name="session">The scoped Marten document session.</param>
    /// <param name="services">The service provider of the scope <paramref name="session"/> belongs to.</param>
    public MartenUnitOfWork(IDocumentSession session, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        this._session = session;
        this._services = services;
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (this._services is not null)
        {
            // This scope, whatever else is current: the listener resolves the domain event handlers from it, and a handler
            // that writes must write to the session being saved. Set inside this async method, the value does not flow
            // back to the caller.
            MartenScopeAccessor.Current = this._services;
        }

        // Otherwise Marten would return before the drain ran, and an operation that sent but wrote nothing would lose its
        // messages.
        if (!HasPendingWork(this._session) && OutboxListenerRegistry.ContainsAny(this._session.Listeners))
        {
            this._session.QueueSqlCommand(DrainTriggerSql);
        }

        await this._session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gets whether <paramref name="session"/> has operations or events queued, which is what makes Marten save it.
    /// </summary>
    private static bool HasPendingWork(IDocumentSession session)
    {
        var pendingChanges = session.PendingChanges;

        return pendingChanges.Operations().Any()
               || pendingChanges.Streams().Any(static stream => stream.Events.Count > 0);
    }
}
