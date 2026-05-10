using Marten;

namespace Blueprint.Marten;

/// <summary>
/// A Marten document session listener that publishes domain events from entities implementing
/// <see cref="IHaveDomainEvents"/> to a message bus before <c>SaveChangesAsync</c>. The bus
/// implementation is resolved from the scoped <see cref="IServiceProvider"/> via
/// <see cref="MartenScopeAccessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// This listener publishes domain events to any <see cref="IDomainEventPublisher"/>
/// registered in DI. Consumers typically implement <see cref="IDomainEventPublisher"/>
/// by wrapping Rebus's <c>IBus.Publish</c> or a similar message bus API.
/// </para>
/// <para>
/// When used with Rebus's outbox pattern (where the Marten session is enrolled as the
/// outbox storage), published messages are committed atomically with the document session's
/// database transaction, providing exactly-once delivery guarantees.
/// </para>
/// <para>
/// This listener runs <b>after</b> <see cref="DomainEventDispatchingSessionListener"/> in the
/// <c>BeforeSaveChangesAsync</c> chain, so in-memory handlers have already processed the events
/// before they are published to the bus.
/// </para>
/// </remarks>
public sealed class OutboxPublishingSessionListener : DocumentSessionListenerBase
{
    /// <inheritdoc />
    public override async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        var serviceProvider = MartenScopeAccessor.Current;
        if (serviceProvider is null)
        {
            return;
        }

        var publisher = (IDomainEventPublisher?)serviceProvider.GetService(typeof(IDomainEventPublisher));
        if (publisher is null)
        {
            return;
        }

        var trackers = session.PendingChanges.AllChangedFor<IHaveDomainEvents>();

        foreach (var entity in trackers)
        {
            var events = entity.GetPendingEvents();
            if (events.Count == 0)
            {
                continue;
            }

            foreach (var domainEvent in events)
            {
                await publisher.PublishAsync(domainEvent, token);
            }
        }
    }
}
