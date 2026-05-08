namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample transaction scope that demonstrates the Rebus outbox pattern used in Signature365.
/// In production, this would wrap a messaging library's transaction scope (e.g. <c>RebusTransactionScope</c>)
/// to ensure that messages are only published after successful command execution.
/// </summary>
/// <remarks>
/// <para>
/// The pattern in legacy Blueprint was:
/// <list type="number">
///   <item><c>StartRebusTransactionScopeMiddleware</c> — creates scope before execution</item>
///   <item>Handler executes and enqueues messages within the scope</item>
///   <item><c>CommitRebusTransactionScopeMiddleware</c> — commits scope after successful execution</item>
/// </list>
/// </para>
/// <para>
/// In the new Blueprint source-generator framework, this pattern is implemented through
/// companion pipeline modules that contribute to the Setup and PostExecution stages.
/// The generated pipeline handles the ordering automatically through the stage system.
/// </para>
/// <para>
/// This sample implementation uses an <see cref="AsyncLocal{T}"/> ambient scope
/// that handlers can access to enqueue messages, demonstrating the same pattern as
/// Signature365's <c>RebusTransactionScope</c> without requiring a dependency on Rebus.
/// </para>
/// </remarks>
public sealed class InMemoryMessageOutbox : IAsyncDisposable
{
    private static readonly AsyncLocal<InMemoryMessageOutbox?> _current = new();
    private readonly List<object> _pendingMessages = [];
    private readonly ILogger<InMemoryMessageOutbox> _logger;
    private bool _committed;

    /// <summary>
    /// Initialises a new outbox scope and makes it the ambient current scope.
    /// </summary>
    /// <param name="logger">Logger for observability.</param>
    public InMemoryMessageOutbox(ILogger<InMemoryMessageOutbox> logger)
    {
        this._logger = logger;
        _current.Value = this;
        this._logger.LogDebug("Message outbox scope started");
    }

    /// <summary>
    /// Gets the current ambient outbox scope, or <c>null</c> if no scope is active.
    /// Handlers use this to enqueue messages that will only be published on commit.
    /// </summary>
    public static InMemoryMessageOutbox? Current => _current.Value;

    /// <summary>
    /// Enqueues a message to be published when the scope is committed.
    /// In production, this would delegate to the messaging library's outbox.
    /// </summary>
    /// <param name="message">The message to enqueue for deferred publication.</param>
    public void Publish(object message)
    {
        this._pendingMessages.Add(message);
        this._logger.LogDebug("Message enqueued in outbox: {MessageType}", message.GetType().Name);
    }

    /// <summary>
    /// Commits the outbox scope, publishing all enqueued messages.
    /// In production, this would call <c>RebusTransactionScope.CompleteAsync()</c>.
    /// </summary>
    /// <returns>A task representing the asynchronous commit operation.</returns>
    public Task CommitAsync()
    {
        if (this._pendingMessages.Count > 0)
        {
            this._logger.LogInformation(
                "Committing outbox: publishing {Count} message(s) [{Types}]",
                this._pendingMessages.Count,
                string.Join(", ", this._pendingMessages.Select(static m => m.GetType().Name)));
        }

        this._committed = true;
        this._pendingMessages.Clear();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!this._committed && this._pendingMessages.Count > 0)
        {
            this._logger.LogWarning(
                "Outbox disposed without commit — {Count} message(s) discarded (handler failure or exception)",
                this._pendingMessages.Count);
        }

        _current.Value = null;

        return ValueTask.CompletedTask;
    }
}
