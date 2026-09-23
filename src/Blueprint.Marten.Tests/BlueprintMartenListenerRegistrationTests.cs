using FluentAssertions;
using Marten;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves that the session listeners Blueprint's Marten integration registers, run in the order it registers them,
/// both dispatch an entity's domain events to in-process handlers and publish them to <see cref="IDomainEventPublisher"/>.
/// </summary>
/// <remarks>
/// <para>
/// The listeners are taken from the real registration path (<see cref="MartenServiceCollectionExtensions.AddBlueprintMarten"/>
/// and the <see cref="IConfigureMarten"/> it registers) and run in <see cref="StoreOptions.Listeners"/> order, which is
/// the order Marten runs <c>BeforeSaveChangesAsync</c> in. The session is a substitute rather than one opened on a
/// database: nothing in this repository's test run provides PostgreSQL, and what is under test is the listener chain,
/// not Marten's persistence.
/// </para>
/// <para>
/// The dispatching listener used to clear an entity's events unconditionally, and ran first, so the publishing listener
/// that followed it never saw any: no consumer's domain events were ever published.
/// </para>
/// </remarks>
public class BlueprintMartenListenerRegistrationTests
{
    [Fact]
    public async Task AddBlueprintMarten_listeners_publish_an_entitys_domain_event()
    {
        var recorder = new Recorder();
        await using var provider = BuildProvider(recorder);
        var options = ApplyConfigureMarten(provider, new StoreOptions());

        var entity = new TestEntity();
        entity.RaiseEvent(new OrderPlaced("ORD-1"));

        await RunBeforeSaveChangesAsync(options, provider, entity);

        recorder.Published.Should().ContainSingle().Which.Should().Be(new OrderPlaced("ORD-1"));
    }

    [Fact]
    public async Task AddBlueprintMarten_listeners_dispatch_then_publish_each_event_once_and_then_clear_it()
    {
        var recorder = new Recorder();
        await using var provider = BuildProvider(recorder);
        var options = ApplyConfigureMarten(provider, new StoreOptions());

        var entity = new TestEntity();
        entity.RaiseEvent(new OrderPlaced("ORD-1"));
        entity.RaiseEvent(new OrderPlaced("ORD-2"));

        await RunBeforeSaveChangesAsync(options, provider, entity);

        recorder.Log.Should().Equal("handled ORD-1", "handled ORD-2", "published ORD-1", "published ORD-2");
        entity.GetPendingEvents().Should().BeEmpty("the events have been dispatched and published");
    }

    [Fact]
    public async Task Consumer_built_store_options_do_not_receive_listeners_from_AddBlueprintMarten()
    {
        // Signature365's shape: StoreOptions is registered as a singleton built by the consumer, and the DocumentStore
        // is constructed from it, so the IConfigureMarten that ConfigureMarten registers is never applied.
        var services = new ServiceCollection();
        services.AddSingleton(_ => new StoreOptions());
        services.AddBlueprintMarten();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<StoreOptions>().Listeners
            .Should().NotContain(static listener => listener is DomainEventDispatchingSessionListener);
    }

    [Fact]
    public async Task AddBlueprintSessionListeners_on_consumer_built_store_options_attaches_the_listener()
    {
        var recorder = new Recorder();
        await using var provider = BuildProvider(recorder);
        var options = new StoreOptions().AddBlueprintSessionListeners();

        var entity = new TestEntity();
        entity.RaiseEvent(new OrderPlaced("ORD-1"));

        await RunBeforeSaveChangesAsync(options, provider, entity);

        recorder.Log.Should().Equal("handled ORD-1", "published ORD-1");
    }

    [Fact]
    public async Task AddBlueprintSessionListeners_and_AddBlueprintMarten_together_dispatch_and_publish_each_event_once()
    {
        var recorder = new Recorder();
        await using var provider = BuildProvider(recorder);
        var options = ApplyConfigureMarten(provider, new StoreOptions().AddBlueprintSessionListeners());

        var entity = new TestEntity();
        entity.RaiseEvent(new OrderPlaced("ORD-1"));

        await RunBeforeSaveChangesAsync(options, provider, entity);

        options.Listeners.OfType<DomainEventDispatchingSessionListener>().Should().ContainSingle();
        recorder.Log.Should().Equal("handled ORD-1", "published ORD-1");
    }

    [Fact]
    public async Task When_the_publisher_throws_then_the_entitys_events_stay_pending()
    {
        var recorder = new Recorder { PublishFailure = new InvalidOperationException("The bus is unavailable.") };
        await using var provider = BuildProvider(recorder);
        var options = ApplyConfigureMarten(provider, new StoreOptions());

        var entity = new TestEntity();
        entity.RaiseEvent(new OrderPlaced("ORD-1"));

        var save = () => RunBeforeSaveChangesAsync(options, provider, entity);

        await save.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("The bus is unavailable.");
        entity.GetPendingEvents().Should().ContainSingle("an event that was never published must not be cleared as if it had been");
    }

    [Fact]
    public async Task When_a_handler_raises_an_event_on_the_entity_being_dispatched_then_the_save_fails_rather_than_discarding_it()
    {
        var recorder = new Recorder();
        var entity = new TestEntity();
        recorder.OnHandled = orderId => entity.RaiseEvent(new OrderPlaced($"{orderId}-follow-up"));
        await using var provider = BuildProvider(recorder);
        var options = ApplyConfigureMarten(provider, new StoreOptions());

        entity.RaiseEvent(new OrderPlaced("ORD-1"));

        var save = () => RunBeforeSaveChangesAsync(options, provider, entity);

        await save.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*changed the pending domain events of the TestEntity*");
        entity.GetPendingEvents().Should().Contain(new OrderPlaced("ORD-1-follow-up"), "the raised event must not be cleared unseen");
        recorder.Published.Should().BeEmpty("a save that is going to fail must not have published the entity's events first");
    }

    private static ServiceProvider BuildProvider(Recorder recorder)
    {
        var services = new ServiceCollection();
        services.AddBlueprintMarten();
        services.AddSingleton(recorder);
        services.AddTransient<IDomainEventHandler<OrderPlaced>, RecordingHandler>();
        services.AddSingleton<IDomainEventPublisher, RecordingPublisher>();

        return services.BuildServiceProvider();
    }

    private static StoreOptions ApplyConfigureMarten(IServiceProvider provider, StoreOptions options)
    {
        foreach (var configure in provider.GetServices<IConfigureMarten>())
        {
            configure.Configure(provider, options);
        }

        return options;
    }

    private static async Task RunBeforeSaveChangesAsync(StoreOptions options, IServiceProvider provider, TestEntity entity)
    {
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

    private sealed class Recorder
    {
        public List<string> Log { get; } = [];

        public List<IDomainEvent> Published { get; } = [];

        public Exception? PublishFailure { get; init; }

        public Action<string>? OnHandled { get; set; }
    }

    private sealed class RecordingHandler(Recorder recorder) : IDomainEventHandler<OrderPlaced>
    {
        public ValueTask HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
        {
            recorder.Log.Add($"handled {domainEvent.OrderId}");
            recorder.OnHandled?.Invoke(domainEvent.OrderId);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPublisher(Recorder recorder) : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            if (recorder.PublishFailure is not null)
            {
                throw recorder.PublishFailure;
            }

            recorder.Published.Add(domainEvent);
            recorder.Log.Add($"published {((OrderPlaced)domainEvent).OrderId}");

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
