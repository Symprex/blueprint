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
/// </remarks>
public sealed class MartenUnitOfWork : Persistence.IUnitOfWork
{
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

        await this._session.SaveChangesAsync(cancellationToken);
    }
}
