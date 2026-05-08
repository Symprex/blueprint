using JetBrains.Annotations;

namespace Blueprint.Messaging;

/// <summary>
/// Represents a transactional outbox for messages that should be published only after
/// successful command execution. Implementations typically wrap a messaging library's
/// transaction scope (for example Rebus's <c>RebusTransactionScope</c>) so that
/// enqueued messages are committed atomically with the handler's side-effects.
/// </summary>
/// <remarks>
/// <para>
/// Register an implementation as a <b>scoped</b> service so each operation execution
/// gets its own outbox instance. The generated pipeline calls
/// <see cref="CommitAsync"/> in the PostExecution stage only when the handler
/// reports success (<c>handlerResult.WasSuccessful</c>).
/// </para>
/// <para>
/// If <see cref="CommitAsync"/> is not called (for example because the handler
/// threw or returned a failure result), the outbox should discard any pending
/// messages when it is disposed by the DI scope.
/// </para>
/// </remarks>
[PublicAPI]
public interface IMessageOutbox
{
    /// <summary>
    /// Commits any messages that were enqueued during handler execution,
    /// making them visible to consumers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token propagated from the operation context.</param>
    /// <returns>A task that completes when all pending messages have been committed.</returns>
    Task CommitAsync(CancellationToken cancellationToken);
}
