using System.Collections.Concurrent;
using System.Reflection;
using Marten;

namespace Blueprint.Marten;

/// <summary>
/// A Marten document session listener that dispatches domain events raised by entities
/// implementing <see cref="IHaveDomainEvents"/> before <c>SaveChangesAsync</c> commits.
/// Events are dispatched to registered <see cref="IDomainEventHandler{TEvent}"/> implementations
/// resolved from the <see cref="IServiceProvider"/> stored in the current
/// <see cref="MartenScopeAccessor"/>.
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
                await DispatchEventAsync(serviceProvider, domainEvent, token);
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
