using Blueprint.Persistence;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IUnitOfWork"/> demonstrating Marten's <c>IDocumentSession.SaveChangesAsync()</c>
/// pattern. In production, the consumer registers a thin adapter around their document session or
/// EF Core <c>DbContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the legacy <c>SaveMartenChangesMiddleware</c> from Signature365.
/// The generated pipeline calls <see cref="SaveChangesAsync"/> automatically:
/// <list type="bullet">
/// <item>After successful command execution (handler returned non-error result)</item>
/// <item>Before background task flush (so tasks see committed state)</item>
/// <item>Skipped for queries, validation failures, and <c>[DoNotSaveChanges]</c> operations</item>
/// </list>
/// </para>
/// <para>
/// In Signature365, the same session is injected into both the handler (for domain writes) and
/// the Rebus outbox (for atomic message dispatch), ensuring messages and data changes are committed
/// or rolled back together. This demonstrates that seam.
/// </para>
/// </remarks>
public sealed class SampleMartenUnitOfWork(ILogger<SampleMartenUnitOfWork> logger) : IUnitOfWork
{
    private readonly List<DocumentChange> _pendingChanges = [];

    /// <summary>
    /// Tracks a document store operation (analogous to <c>IDocumentSession.Store()</c>).
    /// </summary>
    /// <param name="entityType">The type of entity being persisted.</param>
    /// <param name="id">The entity identifier.</param>
    public void Store(string entityType, string id)
    {
        this._pendingChanges.Add(new DocumentChange("Store", entityType, id));
        logger.LogDebug("Marten: Store {EntityType} #{Id}", entityType, id);
    }

    /// <summary>
    /// Tracks a document delete operation (analogous to <c>IDocumentSession.Delete()</c>).
    /// </summary>
    /// <param name="entityType">The type of entity being deleted.</param>
    /// <param name="id">The entity identifier.</param>
    public void Delete(string entityType, string id)
    {
        this._pendingChanges.Add(new DocumentChange("Delete", entityType, id));
        logger.LogDebug("Marten: Delete {EntityType} #{Id}", entityType, id);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (this._pendingChanges.Count == 0)
        {
            logger.LogDebug("Marten: SaveChangesAsync called with no pending changes");
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "💾 Marten SAVE: {Count} change(s) committed → [{Changes}]",
            this._pendingChanges.Count,
            string.Join(", ", this._pendingChanges.Select(static c => $"{c.Operation}({c.EntityType}#{c.Id})")));

        this._pendingChanges.Clear();
        return Task.CompletedTask;
    }

    private sealed record DocumentChange(string Operation, string EntityType, string Id);
}
