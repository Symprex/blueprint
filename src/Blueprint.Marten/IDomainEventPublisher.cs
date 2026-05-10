using JetBrains.Annotations;

namespace Blueprint.Marten;

/// <summary>
/// Publishes domain events to an external message bus (e.g. Rebus, MassTransit) during
/// Marten's <c>BeforeSaveChangesAsync</c> lifecycle. Implementations are resolved from DI
/// by the <see cref="OutboxPublishingSessionListener"/>.
/// </summary>
/// <remarks>
/// <para>
/// When using Rebus, a typical implementation wraps <c>IBus.Publish</c>. When the Marten
/// session is enrolled as a Rebus outbox, the published messages are committed atomically
/// with the document session's database transaction.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDomainEventPublisher
{
    /// <summary>
    /// Publishes a domain event to the message bus.
    /// </summary>
    /// <param name="domainEvent">The domain event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the event has been enqueued.</returns>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}
