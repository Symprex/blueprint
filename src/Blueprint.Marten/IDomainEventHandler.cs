namespace Blueprint.Marten;

/// <summary>
/// Handles a domain event dispatched by the <see cref="DomainEventDispatchingSessionListener"/>
/// before <c>SaveChangesAsync</c>. Handlers run in-process within the same unit-of-work scope,
/// so any changes they make to entities or the document session are committed atomically.
/// </summary>
/// <typeparam name="TEvent">The domain event type to handle.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Processes the domain event within the current unit-of-work scope.
    /// </summary>
    /// <param name="event">The domain event raised by an entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when handling is finished.</returns>
    ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
