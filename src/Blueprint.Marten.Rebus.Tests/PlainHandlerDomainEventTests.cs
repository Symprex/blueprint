using Blueprint.Model;
using Blueprint.Rebus;
using FluentAssertions;
using Marten;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Transport;
using Xunit;

namespace Blueprint.Marten.Rebus.Tests;

/// <summary>
/// Proves that a domain event raised by a plain Rebus <see cref="IHandleMessages{TMessage}"/> is dispatched to its
/// in-process handlers and published to the bus when <see cref="BlueprintApiActivator"/> saves the handler's
/// <see cref="MartenUnitOfWork"/> on commit, although no generated pipeline has set <see cref="MartenScopeAccessor"/>.
/// </summary>
/// <remarks>
/// Everything but the session is real: the activator, the unit of work and listener <c>AddBlueprintMarten()</c>
/// registers, and the publisher <c>AddBlueprintMartenRebus()</c> registers. The session is a substitute whose save runs
/// the configured listeners, because nothing in this repository's test run provides PostgreSQL.
/// </remarks>
public class PlainHandlerDomainEventTests
{
    [Fact]
    public async Task Domain_event_raised_by_a_plain_handler_is_dispatched_and_published_when_its_message_commits()
    {
        var recorder = new Recorder();
        var bus = Substitute.For<IBus>();
        await using var provider = BuildProvider(recorder, bus);
        var activator = new BlueprintApiActivator(provider);

        MartenScopeAccessor.Current = null;

        using var transactionScope = new RebusTransactionScope();

        var handlers = await activator.GetHandlers(new PlainMessage("ORD-1"), transactionScope.TransactionContext);
        await handlers.Should().ContainSingle().Subject.Handle(new PlainMessage("ORD-1"));

        MartenScopeAccessor.Current.Should().BeNull("nothing on this path sets it before the save, so the save must");
        recorder.Handled.Should().BeEmpty("the event is dispatched when the unit of work is saved, on commit");

        await transactionScope.CompleteAsync();

        recorder.Handled.Should().Equal("ORD-1");
        await bus.Received(1).Publish(new OrderPlaced("ORD-1"));
    }

    private static ServiceProvider BuildProvider(Recorder recorder, IBus bus)
    {
        var services = new ServiceCollection();

        // An empty app, so the message takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddBlueprintMarten();
        services.AddBlueprintMartenRebus();
        services.AddSingleton(bus);
        services.AddSingleton(recorder);
        services.AddScoped<TestEntity>();
        services.AddScoped(CreateSession);
        services.AddScoped<IHandleMessages<PlainMessage>, RaisingHandler>();
        services.AddTransient<IDomainEventHandler<OrderPlaced>, RecordingHandler>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A session whose save runs the listeners the registered Marten configuration adds, as Marten's would.</summary>
    private static IDocumentSession CreateSession(IServiceProvider services)
    {
        var options = new StoreOptions();

        foreach (var configure in services.GetServices<IConfigureMarten>())
        {
            configure.Configure(services, options);
        }

        var entity = services.GetRequiredService<TestEntity>();
        var pendingChanges = Substitute.For<IUnitOfWork>();
        pendingChanges.AllChangedFor<IHaveDomainEvents>().Returns(_ => new List<IHaveDomainEvents> { entity });

        var session = Substitute.For<IDocumentSession>();
        session.PendingChanges.Returns(pendingChanges);
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(async call =>
        {
            foreach (var listener in options.Listeners)
            {
                await listener.BeforeSaveChangesAsync(session, call.Arg<CancellationToken>());
            }
        });

        return session;
    }

    /// <summary>A message the handler raises an event for.</summary>
    /// <param name="OrderId">The order the event is for.</param>
    public sealed record PlainMessage(string OrderId);

    /// <summary>The domain event raised.</summary>
    /// <param name="OrderId">The order placed.</param>
    public sealed record OrderPlaced(string OrderId) : IDomainEvent;

    private sealed class Recorder
    {
        public List<string> Handled { get; } = [];
    }

    private sealed class RaisingHandler(IDocumentSession session, TestEntity entity) : IHandleMessages<PlainMessage>
    {
        public Task Handle(PlainMessage message)
        {
            session.Store(entity);
            entity.RaiseEvent(new OrderPlaced(message.OrderId));

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler(Recorder recorder) : IDomainEventHandler<OrderPlaced>
    {
        public ValueTask HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
        {
            recorder.Handled.Add(domainEvent.OrderId);

            return ValueTask.CompletedTask;
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
