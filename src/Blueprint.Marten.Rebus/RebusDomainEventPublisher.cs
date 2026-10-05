using Rebus.Bus;

namespace Blueprint.Marten.Rebus;

/// <summary>
/// Publishes domain events to a Rebus bus, bridging Blueprint.Marten's
/// <see cref="IDomainEventPublisher"/> with the Rebus <see cref="IBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DomainEventDispatchingSessionListener"/> publishes through this during <c>SaveChangesAsync</c>, inside the
/// ambient Rebus transaction: the <c>RebusTransactionScope</c> a generated pipeline opens (via the
/// <c>RebusTransactionModule</c>), or the transaction of the received message a plain handler runs in. Rebus holds the
/// messages until that transaction commits, after the save, so an event is never sent for a save that failed.
/// </para>
/// <para>
/// It is not atomic with a save that succeeded, and so not exactly-once: the documents are committed first and the
/// events sent afterwards. If sending fails after the commit, the documents stay and the events are not sent. The
/// delivery of a received message is then retried, re-running its handler; nothing retries an HTTP request. Delivery
/// that is atomic with the save needs an outbox: an <c>IRebusTransactionEnlistment</c> that has the transaction's messages
/// written into the session, by a listener added with
/// <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>, and sent from there.
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
