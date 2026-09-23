using Rebus.Bus;

namespace Blueprint.Marten.Rebus;

/// <summary>
/// Publishes domain events to a Rebus bus, bridging Blueprint.Marten's
/// <see cref="IDomainEventPublisher"/> with the Rebus <see cref="IBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// When the generated pipeline wraps execution in a <c>RebusTransactionScope</c>
/// (via the <c>RebusTransactionModule</c>), messages published through this publisher
/// are captured by the ambient transaction and only sent after the scope completes
/// successfully. Combined with Marten's <see cref="DomainEventDispatchingSessionListener"/>,
/// this provides exactly-once delivery: domain events are published inside the same
/// database transaction as the document changes.
/// </para>
/// </remarks>
public sealed class RebusDomainEventPublisher : IDomainEventPublisher
{
    private readonly IBus _bus;

    /// <summary>
    /// Initialises a new instance with the Rebus bus to publish events to.
    /// </summary>
    /// <param name="bus">The Rebus bus instance.</param>
    public RebusDomainEventPublisher(IBus bus)
    {
        this._bus = bus;
    }

    /// <inheritdoc />
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        return this._bus.Publish(domainEvent);
    }
}
