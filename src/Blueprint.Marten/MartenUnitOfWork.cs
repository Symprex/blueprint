using Marten;

namespace Blueprint.Marten;

/// <summary>
/// Implements <see cref="Blueprint.Persistence.IUnitOfWork"/> by delegating to a Marten
/// <see cref="IDocumentSession"/>. Register as a scoped service so each operation
/// execution gets the same session instance that was injected into the handler.
/// </summary>
/// <remarks>
/// The generated pipeline calls <c>SaveChangesAsync</c> in the PostExecution stage
/// after successful handler execution. The session is disposed by the DI scope.
/// </remarks>
public sealed class MartenUnitOfWork : Persistence.IUnitOfWork
{
    private readonly IDocumentSession _session;

    /// <summary>
    /// Initialises the unit of work with the Marten session for the current scope.
    /// </summary>
    /// <param name="session">The scoped Marten document session.</param>
    public MartenUnitOfWork(IDocumentSession session)
    {
        this._session = session;
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await this._session.SaveChangesAsync(cancellationToken);
    }
}
