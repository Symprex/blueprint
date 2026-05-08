using Blueprint.Persistence;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IUnitOfWork"/> implementation that demonstrates the persistence seam
/// used by the generated pipeline. This proves the Marten/EF Core integration pattern works:
/// the pipeline calls <see cref="SaveChangesAsync"/> after successful command execution,
/// and skips the call for queries, validation failures, or operations marked with
/// <c>[DoNotSaveChanges]</c>.
/// </summary>
/// <remarks>
/// <para>
/// In a production Marten integration, this would wrap <c>IDocumentSession.SaveChangesAsync()</c>.
/// In an EF Core integration, this would wrap <c>DbContext.SaveChangesAsync()</c>.
/// The consumer registers their implementation as a scoped service via
/// <c>blueprint.AddUnitOfWork&lt;MartenUnitOfWork&gt;()</c>.
/// </para>
/// <para>
/// Demonstrates the same pattern as legacy Blueprint's <c>SaveMartenChangesMiddleware</c>
/// from Signature365, where the middleware was responsible for calling
/// <c>IDocumentSession.SaveChangesAsync()</c> after command execution.
/// </para>
/// </remarks>
public sealed class InMemoryUnitOfWork(ILogger<InMemoryUnitOfWork> logger) : IUnitOfWork
{
    private readonly List<string> _pendingChanges = [];

    /// <summary>
    /// Records a pending change to be committed when <see cref="SaveChangesAsync"/> is called.
    /// In a real implementation this would track entity changes in the underlying persistence layer.
    /// </summary>
    /// <param name="description">A human-readable description of the change being tracked.</param>
    public void TrackChange(string description)
    {
        this._pendingChanges.Add(description);
        logger.LogDebug("Change tracked: {Description}", description);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (this._pendingChanges.Count == 0)
        {
            logger.LogDebug("UnitOfWork.SaveChangesAsync called with no pending changes");
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "UnitOfWork.SaveChangesAsync committing {Count} change(s): [{Changes}]",
            this._pendingChanges.Count,
            string.Join(", ", this._pendingChanges));

        this._pendingChanges.Clear();

        return Task.CompletedTask;
    }
}
