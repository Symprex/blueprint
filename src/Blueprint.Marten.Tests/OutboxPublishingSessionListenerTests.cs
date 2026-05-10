using FluentAssertions;
using Marten;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves that <see cref="OutboxPublishingSessionListener"/> publishes domain events to the
/// registered <see cref="IDomainEventPublisher"/> before Marten saves changes.
/// </summary>
public class OutboxPublishingSessionListenerTests
{
    [Fact]
    public async Task BeforeSaveChanges_when_scope_accessor_null_then_does_nothing()
    {
        MartenScopeAccessor.Current = null;
        var session = Substitute.For<IDocumentSession>();
        var sut = new OutboxPublishingSessionListener();

        await sut.BeforeSaveChangesAsync(session, CancellationToken.None);
    }

    [Fact]
    public async Task BeforeSaveChanges_when_no_publisher_registered_then_does_nothing()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        MartenScopeAccessor.Current = sp;

        try
        {
            var entity = new TestEntity();
            entity.RaiseEvent(new TestDomainEvent { Id = "evt-1" });

            var session = CreateSessionWithPendingChanges(entity);
            var sut = new OutboxPublishingSessionListener();

            // Should not throw
            await sut.BeforeSaveChangesAsync(session, CancellationToken.None);
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    [Fact]
    public async Task BeforeSaveChanges_publishes_all_events_to_publisher()
    {
        var publisher = new TestPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(publisher);
        var sp = services.BuildServiceProvider();
        MartenScopeAccessor.Current = sp;

        try
        {
            var entity = new TestEntity();
            entity.RaiseEvent(new TestDomainEvent { Id = "evt-1" });
            entity.RaiseEvent(new TestDomainEvent { Id = "evt-2" });

            var session = CreateSessionWithPendingChanges(entity);
            var sut = new OutboxPublishingSessionListener();

            await sut.BeforeSaveChangesAsync(session, CancellationToken.None);

            publisher.PublishedEvents.Should().HaveCount(2);
            publisher.PublishedEvents[0].Should().BeOfType<TestDomainEvent>()
                .Which.Id.Should().Be("evt-1");
            publisher.PublishedEvents[1].Should().BeOfType<TestDomainEvent>()
                .Which.Id.Should().Be("evt-2");
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    [Fact]
    public async Task BeforeSaveChanges_does_not_clear_events()
    {
        // OutboxPublishingSessionListener should NOT clear events — that's the dispatching listener's job
        var publisher = new TestPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventPublisher>(publisher);
        var sp = services.BuildServiceProvider();
        MartenScopeAccessor.Current = sp;

        try
        {
            var entity = new TestEntity();
            entity.RaiseEvent(new TestDomainEvent { Id = "evt-1" });

            var session = CreateSessionWithPendingChanges(entity);
            var sut = new OutboxPublishingSessionListener();

            await sut.BeforeSaveChangesAsync(session, CancellationToken.None);

            // Events should still be present (clearing is dispatching listener's responsibility)
            entity.GetPendingEvents().Should().ContainSingle();
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

    private class TestDomainEvent : IDomainEvent
    {
        public required string Id { get; init; }
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

    private class TestPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            this.PublishedEvents.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
