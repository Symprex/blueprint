using FluentAssertions;
using Marten;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves that <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/> puts a consumer's outbox listener
/// after <see cref="DomainEventDispatchingSessionListener"/>, which publishes the domain events the outbox must write, and
/// keeps it there when <c>AddBlueprintMarten()</c>'s configuration is applied afterwards.
/// </summary>
/// <remarks>
/// Marten runs <c>BeforeSaveChangesAsync</c> in <see cref="StoreOptions.Listeners"/> order. The "outbox" here is a list the
/// publisher appends to and the drain listener reads, standing in for a Rebus transaction and the listener that writes
/// its messages into the session.
/// </remarks>
public class OutboxListenerOrderingTests
{
    [Fact]
    public async Task AddBlueprintOutboxListener_then_AddBlueprintMarten_configuration_drains_the_published_event()
    {
        // As a consumer's AddMarten(options => ...) callback would, before the ConfigureMarten callbacks run.
        var outbox = new Outbox();
        await using var provider = BuildProvider(outbox);
        var drain = new DrainListener(outbox);
        var options = new StoreOptions().AddBlueprintOutboxListener(drain);

        ApplyConfigureMarten(provider, options);

        options.Listeners.OfType<DomainEventDispatchingSessionListener>().Should().ContainSingle();
        IndexOf<DomainEventDispatchingSessionListener>(options).Should().BeLessThan(options.Listeners.IndexOf(drain));

        await RunBeforeSaveChangesAsync(options, provider, new OrderPlaced("ORD-1"));

        drain.Drained.Should().Equal(new OrderPlaced("ORD-1"));
    }

    [Fact]
    public async Task Outbox_listener_added_directly_before_AddBlueprintMarten_configuration_drains_nothing()
    {
        // The hazard AddBlueprintOutboxListener exists for: the ConfigureMarten callback appends the dispatching listener
        // after the drain, so the drain runs before anything is published.
        var outbox = new Outbox();
        await using var provider = BuildProvider(outbox);
        var drain = new DrainListener(outbox);
        var options = new StoreOptions();
        options.Listeners.Add(drain);

        ApplyConfigureMarten(provider, options);

        await RunBeforeSaveChangesAsync(options, provider, new OrderPlaced("ORD-1"));

        outbox.Messages.Should().ContainSingle("the event was published");
        drain.Drained.Should().BeEmpty("the drain ran before the event was published");
    }

    [Fact]
    public async Task AddBlueprintOutboxListener_moves_a_listener_already_before_the_dispatching_listener_after_it()
    {
        var outbox = new Outbox();
        await using var provider = BuildProvider(outbox);
        var drain = new DrainListener(outbox);
        var options = new StoreOptions();
        options.Listeners.Add(drain);
        ApplyConfigureMarten(provider, options);

        options.AddBlueprintOutboxListener(drain);

        options.Listeners.Should().ContainSingle(listener => ReferenceEquals(listener, drain));
        IndexOf<DomainEventDispatchingSessionListener>(options).Should().BeLessThan(options.Listeners.IndexOf(drain));
    }

    [Fact]
    public void AddBlueprintOutboxListener_twice_adds_each_listener_once()
    {
        var drain = new DrainListener(new Outbox());
        var options = new StoreOptions();

        options.AddBlueprintOutboxListener(drain).AddBlueprintOutboxListener(drain);

        options.Listeners.Should().ContainSingle(listener => ReferenceEquals(listener, drain));
        options.Listeners.OfType<DomainEventDispatchingSessionListener>().Should().ContainSingle();
    }

    private static int IndexOf<TListener>(StoreOptions options)
    {
        return options.Listeners.ToList().FindIndex(static listener => listener is TListener);
    }

    private static ServiceProvider BuildProvider(Outbox outbox)
    {
        var services = new ServiceCollection();
        services.AddBlueprintMarten();
        services.AddSingleton(outbox);
        services.AddSingleton<IDomainEventPublisher, OutboxPublisher>();

        return services.BuildServiceProvider();
    }

    private static void ApplyConfigureMarten(IServiceProvider provider, StoreOptions options)
    {
        foreach (var configure in provider.GetServices<IConfigureMarten>())
        {
            configure.Configure(provider, options);
        }
    }

    private static async Task RunBeforeSaveChangesAsync(StoreOptions options, IServiceProvider provider, IDomainEvent domainEvent)
    {
        var entity = new TestEntity();
        entity.RaiseEvent(domainEvent);

        var session = Substitute.For<IDocumentSession>();
        var pendingChanges = Substitute.For<IUnitOfWork>();
        pendingChanges.AllChangedFor<IHaveDomainEvents>().Returns(_ => new List<IHaveDomainEvents> { entity });
        session.PendingChanges.Returns(pendingChanges);

        MartenScopeAccessor.Current = provider;

        try
        {
            foreach (var listener in options.Listeners)
            {
                await listener.BeforeSaveChangesAsync(session, CancellationToken.None);
            }
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    private sealed record OrderPlaced(string OrderId) : IDomainEvent;

    private sealed class Outbox
    {
        public List<IDomainEvent> Messages { get; } = [];
    }

    private sealed class OutboxPublisher(Outbox outbox) : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            outbox.Messages.Add(domainEvent);

            return Task.CompletedTask;
        }
    }

    private sealed class DrainListener(Outbox outbox) : DocumentSessionListenerBase
    {
        public List<IDomainEvent> Drained { get; } = [];

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            this.Drained.AddRange(outbox.Messages);

            return Task.CompletedTask;
        }
    }

    private sealed class TestEntity : IHaveDomainEvents
    {
        private readonly List<IDomainEvent> _events = [];

        public void RaiseEvent(IDomainEvent domainEvent)
        {
            this._events.Add(domainEvent);
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
}
