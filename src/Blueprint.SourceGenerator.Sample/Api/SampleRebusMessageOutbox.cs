using Blueprint.Messaging;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IMessageOutbox"/> demonstrating the Rebus transactional outbox pattern.
/// In a real consumer, this would wrap Rebus's <c>RebusTransactionScope</c> to ensure messages
/// are stored atomically with the domain persistence (Marten/EF Core) and forwarded asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// This is the equivalent of legacy Blueprint's <c>StartRebusTransactionScopeMiddleware</c> /
/// <c>CommitRebusTransactionScopeMiddleware</c> pipeline pattern from Signature365.
/// In production:
/// <list type="number">
/// <item>Pipeline opens a Rebus transaction scope (this outbox's constructor or <c>BeginAsync</c>)</item>
/// <item>Handler publishes domain events via <c>IBus.Publish()</c> — messages go to the outbox table</item>
/// <item>Pipeline calls <c>CommitAsync</c> — flushes outbox atomically with <c>IDocumentSession.SaveChangesAsync</c></item>
/// <item><c>OutboxForwarder</c> background service polls and forwards stored messages</item>
/// </list>
/// </para>
/// <para>
/// The generated pipeline calls <see cref="CommitAsync"/> in the PostExecution stage
/// (after handler success and after unit-of-work save). If the handler fails, the
/// outbox is never committed and messages are discarded.
/// </para>
/// </remarks>
public sealed class SampleRebusMessageOutbox(ILogger<SampleRebusMessageOutbox> logger) : IMessageOutbox
{
    private readonly List<object> _pendingMessages = [];

    /// <summary>
    /// Queues a message for publication once the outbox transaction commits.
    /// In production, this would call <c>bus.Publish(message)</c> within the Rebus transaction scope.
    /// </summary>
    /// <param name="message">The domain event or command to publish.</param>
    public void Publish(object message)
    {
        this._pendingMessages.Add(message);
        logger.LogDebug("Outbox: message queued for publish: {MessageType}", message.GetType().Name);
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (this._pendingMessages.Count == 0)
        {
            logger.LogDebug("Outbox: CommitAsync called with no pending messages");
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "📨 Outbox COMMIT: {Count} message(s) forwarded → [{Types}]",
            this._pendingMessages.Count,
            string.Join(", ", this._pendingMessages.Select(static m => m.GetType().Name)));

        this._pendingMessages.Clear();
        return Task.CompletedTask;
    }
}
