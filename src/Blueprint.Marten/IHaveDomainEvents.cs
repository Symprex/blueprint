namespace Blueprint.Marten;

/// <summary>
/// Interface for entities that can raise domain events during a Marten session.
/// Events are collected during handler execution and dispatched by
/// <see cref="DomainEventDispatchingSessionListener"/> before <c>SaveChangesAsync</c>.
/// </summary>
public interface IHaveDomainEvents
{
    /// <summary>
    /// Gets the pending domain events that have been raised during the current unit of work.
    /// </summary>
    /// <returns>The list of pending domain events.</returns>
    IReadOnlyList<IDomainEvent> GetPendingEvents();

    /// <summary>
    /// Clears all pending events after they have been dispatched.
    /// </summary>
    void ClearPendingEvents();
}
