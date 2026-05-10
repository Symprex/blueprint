namespace Blueprint.Marten;

/// <summary>
/// Marker interface for domain events raised by aggregate roots during a Marten session.
/// Domain events are collected during handler execution and dispatched before
/// <c>IDocumentSession.SaveChangesAsync</c> via registered
/// <see cref="IDomainEventHandler{TEvent}"/> implementations.
/// </summary>
public interface IDomainEvent;
