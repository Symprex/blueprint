namespace Blueprint.Testing;

/// <summary>
/// A test-only <see cref="Persistence.IUnitOfWork"/> that tracks whether <c>SaveChangesAsync</c>
/// was called and how many times, without performing any actual persistence. Register this in
/// test DI to verify that the generated pipeline commits work correctly.
/// </summary>
/// <remarks>
/// This replaces the need for a real Marten session or database in tests that only need to
/// verify that the pipeline calls save at the right point.
/// </remarks>
public sealed class InMemoryUnitOfWork : Persistence.IUnitOfWork
{
    private int _saveCount;

    /// <summary>
    /// Gets the number of times <see cref="SaveChangesAsync"/> has been called.
    /// </summary>
    public int SaveCount => this._saveCount;

    /// <summary>
    /// Gets a value indicating whether <see cref="SaveChangesAsync"/> has been called at least once.
    /// </summary>
    public bool WasSaved => this._saveCount > 0;

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref this._saveCount);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resets the save counter. Useful between test executions in shared fixture scenarios.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref this._saveCount, 0);
    }
}
