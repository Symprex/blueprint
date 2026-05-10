using FluentAssertions;
using Marten;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves that <see cref="DomainEventDispatchingSessionListener"/> dispatches domain events
/// to registered handlers via the scoped service provider set in <see cref="MartenScopeAccessor"/>.
/// </summary>
public class DomainEventDispatchingSessionListenerTests
{
    [Fact]
    public async Task BeforeSaveChanges_when_scope_accessor_null_then_does_nothing()
    {
        MartenScopeAccessor.Current = null;
        var session = Substitute.For<IDocumentSession>();
        var sut = new DomainEventDispatchingSessionListener();

        // Should not throw even with no scope
        await sut.BeforeSaveChangesAsync(session, CancellationToken.None);
    }

    [Fact]
    public async Task BeforeSaveChanges_dispatches_events_to_all_registered_handlers()
    {
        var handler1 = new TestOrderCreatedHandler();
        var handler2 = new TestOrderCreatedHandler();

        var services = new ServiceCollection();
        services.AddTransient<IDomainEventHandler<OrderCreatedEvent>>(_ => handler1);
        services.AddTransient<IDomainEventHandler<OrderCreatedEvent>>(_ => handler2);
        var sp = services.BuildServiceProvider();

        MartenScopeAccessor.Current = sp;

        try
        {
            var entity = new TestEntity();
            entity.RaiseEvent(new OrderCreatedEvent { OrderId = "ORD-123" });

            var session = CreateSessionWithPendingChanges(entity);
            var sut = new DomainEventDispatchingSessionListener();

            await sut.BeforeSaveChangesAsync(session, CancellationToken.None);

            handler1.HandledEvents.Should().ContainSingle()
                .Which.OrderId.Should().Be("ORD-123");
            handler2.HandledEvents.Should().ContainSingle()
                .Which.OrderId.Should().Be("ORD-123");
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    [Fact]
    public async Task BeforeSaveChanges_clears_pending_events_after_dispatch()
    {
        var handler = new TestOrderCreatedHandler();
        var services = new ServiceCollection();
        services.AddTransient<IDomainEventHandler<OrderCreatedEvent>>(_ => handler);
        var sp = services.BuildServiceProvider();

        MartenScopeAccessor.Current = sp;

        try
        {
            var entity = new TestEntity();
            entity.RaiseEvent(new OrderCreatedEvent { OrderId = "ORD-1" });

            var session = CreateSessionWithPendingChanges(entity);
            var sut = new DomainEventDispatchingSessionListener();

            await sut.BeforeSaveChangesAsync(session, CancellationToken.None);

            entity.GetPendingEvents().Should().BeEmpty();
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    [Fact]
    public async Task BeforeSaveChanges_when_no_handlers_registered_then_completes_silently()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        MartenScopeAccessor.Current = sp;

        try
        {
            var entity = new TestEntity();
            entity.RaiseEvent(new OrderCreatedEvent { OrderId = "ORD-1" });

            var session = CreateSessionWithPendingChanges(entity);
            var sut = new DomainEventDispatchingSessionListener();

            // Should not throw when no handlers are registered
            await sut.BeforeSaveChangesAsync(session, CancellationToken.None);

            entity.GetPendingEvents().Should().BeEmpty();
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    [Fact]
    public async Task BeforeSaveChanges_dispatches_multiple_event_types()
    {
        var orderHandler = new TestOrderCreatedHandler();
        var cancelledHandler = new TestOrderCancelledHandler();

        var services = new ServiceCollection();
        services.AddTransient<IDomainEventHandler<OrderCreatedEvent>>(_ => orderHandler);
        services.AddTransient<IDomainEventHandler<OrderCancelledEvent>>(_ => cancelledHandler);
        var sp = services.BuildServiceProvider();

        MartenScopeAccessor.Current = sp;

        try
        {
            var entity = new TestEntity();
            entity.RaiseEvent(new OrderCreatedEvent { OrderId = "ORD-1" });
            entity.RaiseEvent(new OrderCancelledEvent { OrderId = "ORD-1", Reason = "changed mind" });

            var session = CreateSessionWithPendingChanges(entity);
            var sut = new DomainEventDispatchingSessionListener();

            await sut.BeforeSaveChangesAsync(session, CancellationToken.None);

            orderHandler.HandledEvents.Should().ContainSingle();
            cancelledHandler.HandledEvents.Should().ContainSingle()
                .Which.Reason.Should().Be("changed mind");
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    private static IDocumentSession CreateSessionWithPendingChanges(TestEntity entity)
    {
        var session = Substitute.For<IDocumentSession>();
        var pendingChanges = Substitute.For<IUnitOfWork>();
        pendingChanges.AllChangedFor<IHaveDomainEvents>()
            .Returns(new List<IHaveDomainEvents> { entity });
        session.PendingChanges.Returns(pendingChanges);
        return session;
    }

    // Test types
    private class OrderCreatedEvent : IDomainEvent
    {
        public required string OrderId { get; init; }
    }

    private class OrderCancelledEvent : IDomainEvent
    {
        public required string OrderId { get; init; }

        public required string Reason { get; init; }
    }

    private class TestEntity : IHaveDomainEvents
    {
        private readonly List<IDomainEvent> _events = [];

        public void RaiseEvent(IDomainEvent @event)
        {
            this._events.Add(@event);
        }

        public IReadOnlyList<IDomainEvent> GetPendingEvents()
        {
            return this._events;
        }

        public void ClearPendingEvents()
        {
            this._events.Clear();
        }
    }

    private class TestOrderCreatedHandler : IDomainEventHandler<OrderCreatedEvent>
    {
        public List<OrderCreatedEvent> HandledEvents { get; } = [];

        public ValueTask HandleAsync(OrderCreatedEvent domainEvent, CancellationToken cancellationToken)
        {
            this.HandledEvents.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }

    private class TestOrderCancelledHandler : IDomainEventHandler<OrderCancelledEvent>
    {
        public List<OrderCancelledEvent> HandledEvents { get; } = [];

        public ValueTask HandleAsync(OrderCancelledEvent domainEvent, CancellationToken cancellationToken)
        {
            this.HandledEvents.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }
}
