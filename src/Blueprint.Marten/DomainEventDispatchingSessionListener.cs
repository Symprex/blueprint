using System.Collections.Concurrent;
using System.Reflection;
using Marten;

namespace Blueprint.Marten;

/// <summary>
/// A Marten document session listener that, before <c>SaveChangesAsync</c> commits, takes the domain events raised by
/// entities implementing <see cref="IHaveDomainEvents"/>, dispatches them to the registered
/// <see cref="IDomainEventHandler{TEvent}"/> implementations, publishes them to the registered
/// <see cref="IDomainEventPublisher"/> (when there is one), and only then clears them. Both are resolved from the
/// <see cref="IServiceProvider"/> stored in the current <see cref="MartenScopeAccessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// This listener runs as part of <c>BeforeSaveChangesAsync</c>, meaning domain event
/// handlers execute within the same transaction scope. Handlers can modify entities
/// or store additional documents in the session — those changes are included in the
/// same commit.
/// </para>
/// <para>
/// Handler resolution uses the open generic <c>IDomainEventHandler&lt;TEvent&gt;</c> interface
/// and dispatches to all registered implementations for each event type.
/// </para>
/// <para>
/// Dispatching, publishing and clearing belong to this one listener because clearing is what makes an event invisible
/// to everything after it. They used to be split across two listeners, and the dispatching one — registered first —
/// cleared every entity's events, so the publishing one never saw any and no event was ever published. An event is
/// dispatched to every handler before any is published, so in-process handlers have run by the time the bus sees it,
/// and it is cleared only once both have succeeded: a handler or publisher that throws leaves the entity's events
/// pending, as the failed save leaves its documents.
/// </para>
/// <para>
/// A handler must not raise a further event on the entity whose events are being dispatched: clearing would discard it
/// unseen, so the save fails with an <see cref="InvalidOperationException"/> instead, as it always has (it used to
/// fail with the list's own "collection was modified").
/// </para>
/// <para>
/// <see cref="OutboxPublishingSessionListener"/> remains for a consumer that wants publishing without in-process
/// dispatch. Registered after this listener, it finds nothing left to publish, so combining the two does not publish
/// an event twice.
/// </para>
/// </remarks>
public sealed class DomainEventDispatchingSessionListener : DocumentSessionListenerBase
{
    // Cache the constructed generic types and method info per event type to avoid
    // repeated MakeGenericType/GetMethod reflection on every SaveChangesAsync call.
    private static readonly ConcurrentDictionary<Type, EventDispatchInfo> _dispatchCache = new();

    /// <inheritdoc />
    public override async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        var serviceProvider = MartenScopeAccessor.Current;
        if (serviceProvider is null)
        {
            return;
        }

        var publisher = (IDomainEventPublisher?)serviceProvider.GetService(typeof(IDomainEventPublisher));
        var trackers = session.PendingChanges.AllChangedFor<IHaveDomainEvents>();

        foreach (var entity in trackers)
        {
            // A copy, because the pending list is live and a handler could append to it while it is being dispatched.
            var events = entity.GetPendingEvents().ToArray();
            if (events.Length == 0)
            {
                continue;
            }

            foreach (var domainEvent in events)
            {
                await DispatchEventAsync(serviceProvider, domainEvent, token);
            }

            // Clearing would discard, unseen, anything a handler raised on this entity while it was dispatched, so fail
            // the save instead — before anything has been published for it.
            if (!entity.GetPendingEvents().SequenceEqual(events))
            {
                throw new InvalidOperationException(
                    $"A domain event handler changed the pending domain events of the {entity.GetType().Name} whose events were being dispatched. " +
                    "An event raised while dispatching would be neither dispatched nor published by this save, so raise it before saving, " +
                    "or on a different entity.");
            }

            if (publisher is not null)
            {
                foreach (var domainEvent in events)
                {
                    await publisher.PublishAsync(domainEvent, token);
                }
            }

            entity.ClearPendingEvents();
        }
    }

    private static async Task DispatchEventAsync(IServiceProvider serviceProvider, IDomainEvent domainEvent, CancellationToken token)
    {
        var eventType = domainEvent.GetType();
        var dispatchInfo = _dispatchCache.GetOrAdd(eventType, static type =>
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(type);
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
            var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            return new EventDispatchInfo(handlerType, enumerableType, method);
        });

        var handlers = serviceProvider.GetService(dispatchInfo.EnumerableHandlerType);

        if (handlers is null)
        {
            return;
        }

        foreach (var handler in (System.Collections.IEnumerable)handlers)
        {
            var task = (ValueTask)(dispatchInfo.HandleMethod.Invoke(handler, [domainEvent, token]) ?? default(ValueTask));
            await task;
        }
    }

    /// <summary>
    /// Cached reflection metadata for dispatching domain events of a specific type.
    /// </summary>
    private sealed record EventDispatchInfo(Type HandlerType, Type EnumerableHandlerType, MethodInfo HandleMethod);
}
