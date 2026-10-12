using System.Collections.Concurrent;
using Blueprint.Marten.Tests.Postgres;
using Blueprint.Model;
using Blueprint.Rebus;
using FluentAssertions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Timeouts;
using Rebus.Transport;
using Rebus.Transport.InMem;
using Weasel.Core;
using Xunit;

namespace Blueprint.Marten.Rebus.Tests;

/// <summary>
/// Proves that a plain <see cref="IHandleMessages{TMessage}"/> whose message's transaction has an outbox enlisted in it can
/// defer or forward that message while the database is down: the copy is sent once the message has been handled, outside the
/// outbox, so completing the message needs no database when the handler wrote and sent nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Signature365's Billing <c>ActiveUserEventHandler</c> defers its event when Postgres is restarting. Blueprint.Rebus enlists the
/// outbox in a plain handler's own transaction (<see cref="BlueprintApiActivator"/>), and Rebus sends a deferred or forwarded copy
/// in whatever transaction is ambient, so the copy used to be held by the outbox, and the unit of work's save then had a message
/// to write and needed the database that was down: the delivery failed and the event went to the error queue rather than being
/// deferred. Legacy Blueprint ran such a handler in a pipeline scope of its own, with the outbox enlisted there, so its deferral
/// went out in the message's transaction and needed no database (Signature365 PR #7001).
/// </para>
/// <para>
/// Each test runs a real bus on the in-memory transport through <c>AddBlueprintRebus</c>, so the handler is given the
/// <see cref="IBus"/> Blueprint registers. The outbox stands in for Signature365's: a transport decorator that holds a send made
/// in a transaction the outbox is enlisted in, an <see cref="IRebusTransactionEnlistment"/> that enlists it, and a drain, added
/// with <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>, that is an <see cref="IOutboxDrainListener"/>
/// so that a save with nothing held needs no database, as Signature365's <c>MartenOutboxDrainListener</c> is. One worker handles
/// one message at a time, and a failed delivery goes straight to the error queue.
/// </para>
/// </remarks>
public sealed class PlainHandlerDeferralOutboxTests : IAsyncDisposable
{
    private const string InputQueue = "plain-handler-deferral-outbox";
    private const string ForwardQueue = "plain-handler-deferral-outbox-forwarded";
    private const string OrdersPlacedQueue = "plain-handler-deferral-outbox-orders-placed";
    private const string ErrorQueue = "error";

    private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(30);

    private readonly InMemNetwork _network = new();
    private readonly RecordingTimeoutManager _timeoutManager = new();
    private readonly Recorder _recorder = new();

    private ServiceProvider? _services;

    private IBus Bus => this._services!.GetRequiredService<IBus>();

    public async ValueTask DisposeAsync()
    {
        // Disposing the bus stops its workers
        if (this._services is not null)
        {
            await this._services.DisposeAsync();
        }
    }

    [Fact]
    public async Task When_a_handler_defers_its_message_while_the_database_is_down_then_the_copy_is_deferred_with_its_defer_count_incremented()
    {
        // Arrange
        using var store = CreateUnreachableStore();
        this.StartBus(store);
        var messageId = Guid.NewGuid().ToString();

        // Act
        await this.Bus.SendLocal(new DeferredMessage(), WithMessageId(messageId));

        await this.WaitForAsync(() => this._recorder.DeferCounts.Count >= 2);

        // Assert
        this.DeadLetteredErrorDetails().Should().BeNull("deferring the message must not need the database it is deferring for");

        var deferral = this._timeoutManager.Deferrals.Should().ContainSingle().Subject;
        deferral.Headers[Headers.MessageId].Should().Be(messageId, "it is the message being handled that is deferred");
        deferral.Headers[Headers.DeferCount].Should().Be("1");

        this._recorder.DeferCounts.Should().Equal([null, "1"], "the message is handled, deferred, then handled again once it is due");
        this._recorder.HeldSends.Should().Be(0, "the deferred copy must not go through the outbox");
    }

    [Fact]
    public async Task When_a_handler_forwards_its_message_while_the_database_is_down_then_it_arrives_at_the_destination()
    {
        // Arrange
        using var store = CreateUnreachableStore();
        this.StartBus(store);
        var messageId = Guid.NewGuid().ToString();

        // Act
        await this.Bus.SendLocal(new ForwardedMessage(), WithMessageId(messageId));

        var forwarded = await this.WaitForMessageAsync(ForwardQueue);

        // Assert
        this.DeadLetteredErrorDetails().Should().BeNull("forwarding the message must not need the database");

        forwarded.Should().NotBeNull("the handler forwards the message it is handling");
        forwarded!.Headers[Headers.MessageId].Should().Be(messageId);
        this._recorder.HeldSends.Should().Be(0, "the forwarded copy must not go through the outbox");
    }

    [Fact]
    public async Task When_a_handler_defers_its_message_and_then_throws_then_no_deferred_copy_is_sent()
    {
        // Arrange: anything deferred is held, so that it is recorded once and never handled again
        using var store = CreateUnreachableStore();
        this._timeoutManager.MaxHandBacks = 0;
        this.StartBus(store);

        // Act
        await this.Bus.SendLocal(new DeferThenThrowMessage());

        await this.WaitForAsync(() => this._network.GetCount(ErrorQueue) == 1);
        await this.WaitPastMarkerAsync();

        // Assert
        this._recorder.DeferredBeforeThrowing.Should().BeTrue("the deferral itself must succeed for this to prove anything");
        this.DeadLetteredErrorDetails().Should().Contain(DeferThenThrowMessageHandler.Failure, "the handler threw, so the message failed");
        this._timeoutManager.Deferrals.Should().BeEmpty("a deferral belongs to the handling of the message, so a handler that throws discards it");
    }

    [Fact]
    public async Task When_a_handler_that_wrote_to_its_session_defers_while_the_database_is_down_then_the_delivery_fails_and_nothing_is_deferred()
    {
        // Arrange
        using var store = CreateUnreachableStore();
        this._timeoutManager.MaxHandBacks = 0;
        this.StartBus(store);

        // Act
        await this.Bus.SendLocal(new StoreThenDeferMessage("ORD-1"));

        await this.WaitForAsync(() => this._network.GetCount(ErrorQueue) == 1);
        await this.WaitPastMarkerAsync();

        // Assert: the write still needs the database, and the deferral is sent only once the save it follows has succeeded
        this.DeadLetteredErrorDetails().Should().Contain("Npgsql", "saving the handler's write needs the database that is down");
        this._timeoutManager.Deferrals.Should().BeEmpty("a deferral is not sent for a delivery whose save failed");
    }

    [Fact]
    public async Task When_a_handler_that_sent_a_message_defers_while_the_database_is_down_then_the_delivery_fails_and_nothing_is_sent()
    {
        // Arrange
        using var store = CreateUnreachableStore();
        this._timeoutManager.MaxHandBacks = 0;
        this.StartBus(store);

        // Act
        await this.Bus.SendLocal(new SendThenDeferMessage("ORD-1"));

        await this.WaitForAsync(() => this._network.GetCount(ErrorQueue) == 1);
        await this.WaitPastMarkerAsync();

        // Assert: what the handler sends still goes through the outbox, which cannot store it while the database is down. Sending
        // it anyway would send it without the data it announces; deferring alone would lose it
        this.DeadLetteredErrorDetails().Should().Contain("Npgsql", "the outbox needs the database to store the message the handler sent");
        this._network.GetCount(OrdersPlacedQueue).Should().Be(0, "a message of a failed delivery is neither stored nor sent");
        this._timeoutManager.Deferrals.Should().BeEmpty("a deferral is not sent for a delivery whose save failed");
    }

    [PostgresFact]
    public async Task When_a_handler_defers_its_message_while_the_database_is_up_then_the_copy_is_deferred_once_and_not_written_to_the_outbox()
    {
        // Arrange
        await using var database = await PostgresTestStore.CreateAsync();
        this.StartBus(database.Store, database.OutboxTable);
        var messageId = Guid.NewGuid().ToString();

        // Act
        await this.Bus.SendLocal(new DeferredMessage(), WithMessageId(messageId));

        await this.WaitForAsync(() => this._recorder.DeferCounts.Count >= 2);
        await this.WaitPastMarkerAsync();

        // Assert
        this.DeadLetteredErrorDetails().Should().BeNull();

        this._timeoutManager.Deferrals.Should().ContainSingle("the copy is sent once, by the delivery that deferred it")
            .Which.Headers[Headers.MessageId].Should().Be(messageId);

        this._recorder.DeferCounts.Should().Equal([null, "1"]);
        (await database.ReadOutboxIdsAsync()).Should().BeEmpty("the deferred copy is not a message the outbox stores");
        this._recorder.HeldSends.Should().Be(0);
    }

    [PostgresFact]
    public async Task When_a_handler_sends_a_message_and_defers_then_only_the_sent_message_is_written_to_the_outbox()
    {
        // Arrange
        await using var database = await PostgresTestStore.CreateAsync();
        this._timeoutManager.MaxHandBacks = 0;
        this.StartBus(database.Store, database.OutboxTable);
        var messageId = Guid.NewGuid().ToString();

        // Act
        await this.Bus.SendLocal(new SendThenDeferMessage("ORD-1"), WithMessageId(messageId));

        await this.WaitForAsync(() => !this._timeoutManager.Deferrals.IsEmpty || this._network.GetCount(ErrorQueue) > 0);

        // Assert
        this.DeadLetteredErrorDetails().Should().BeNull();

        (await database.ReadOutboxIdsAsync()).Should().Equal(["ORD-1-placed"], "the sent message, and only it, goes through the outbox");
        this._network.GetCount(OrdersPlacedQueue).Should().Be(0, "the sent message went into the outbox, not to the transport");

        this._timeoutManager.Deferrals.Should().ContainSingle()
            .Which.Headers[Headers.MessageId].Should().Be(messageId, "the deferred copy is sent once the outbox has been saved, outside it");
    }

    private static DocumentStore CreateUnreachableStore()
    {
        var options = new StoreOptions();
        options.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1;Pooling=false");
        options.AutoCreateSchemaObjects = AutoCreate.None;

        return new DocumentStore(options);
    }

    private static Dictionary<string, string> WithMessageId(string messageId)
    {
        return new Dictionary<string, string> { [Headers.MessageId] = messageId };
    }

    private void StartBus(DocumentStore store, string outboxTable = "outbox")
    {
        store.Options.AddBlueprintOutboxListener(new DrainListener(outboxTable));

        this._network.CreateQueue(ForwardQueue);
        this._network.CreateQueue(OrdersPlacedQueue);

        var services = new ServiceCollection().AddLogging();

        // An empty app, so every message takes the plain (non-operation) handler path
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton(this._recorder);

        services.AddBlueprintMarten();
        services.AddScoped<IDocumentSession>(_ => store.LightweightSession());
        services.AddScoped<EnlistedTransaction>();
        services.AddScoped<IRebusTransactionEnlistment, OutboxEnlistment>();

        services.AddScoped<IHandleMessages<DeferredMessage>, DeferredMessageHandler>();
        services.AddScoped<IHandleMessages<ForwardedMessage>, ForwardedMessageHandler>();
        services.AddScoped<IHandleMessages<DeferThenThrowMessage>, DeferThenThrowMessageHandler>();
        services.AddScoped<IHandleMessages<StoreThenDeferMessage>, StoreThenDeferMessageHandler>();
        services.AddScoped<IHandleMessages<SendThenDeferMessage>, SendThenDeferMessageHandler>();
        services.AddScoped<IHandleMessages<MarkerMessage>, MarkerMessageHandler>();

        services.AddBlueprintRebus((_, configurer) => configurer
            .Transport(t => t.UseInMemoryTransport(this._network, InputQueue))
            .Routing(static r => r.TypeBased().Map<OrderPlaced>(OrdersPlacedQueue))
            .Timeouts(t => t.Register(_ => this._timeoutManager))
            .Options(o =>
            {
                o.RetryStrategy(maxDeliveryAttempts: 1);
                o.SetNumberOfWorkers(1);
                o.SetMaxParallelism(1);
                o.Decorate<ITransport>(c => new OutboxTransport(c.Get<ITransport>(), this._recorder));
            }));

        this._services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        this._services.StartRebus();
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds, or the message under test has been moved to the error queue, whose error
    /// details say more than a timeout would.
    /// </summary>
    private async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _waitLimit;

        while (!condition() && this._network.GetCount(ErrorQueue) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    private async Task<InMemTransportMessage?> WaitForMessageAsync(string queue)
    {
        var deadline = DateTime.UtcNow + _waitLimit;

        while (DateTime.UtcNow < deadline && this._network.GetCount(ErrorQueue) == 0)
        {
            if (this._network.GetNextOrNull(queue) is { } message)
            {
                return message;
            }

            await Task.Delay(50);
        }

        return null;
    }

    /// <summary>
    /// Sends a marker and waits for it to be handled: with one worker taking one message at a time, anything sent to the input
    /// queue before it, such as a deferred copy on its way to the timeout manager, has been received by then.
    /// </summary>
    private async Task WaitPastMarkerAsync()
    {
        await this.Bus.SendLocal(new MarkerMessage());
        await this._recorder.MarkerHandled.Task.WaitAsync(_waitLimit);
    }

    /// <summary>
    /// Gets the error details of the message moved to the error queue, or <see langword="null"/> when none was.
    /// </summary>
    private string? DeadLetteredErrorDetails()
    {
        return this._network.GetNextOrNull(ErrorQueue) is { } deadLettered
            ? deadLettered.Headers.GetValueOrDefault(Headers.ErrorDetails, "(no error details)")
            : null;
    }

    /// <summary>A message whose handler defers it on its first delivery.</summary>
    public sealed class DeferredMessage;

    /// <summary>A message whose handler forwards it.</summary>
    public sealed class ForwardedMessage;

    /// <summary>A message whose handler defers it, then throws.</summary>
    public sealed class DeferThenThrowMessage;

    /// <summary>A message whose handler stores an order on its session, then defers the message.</summary>
    /// <param name="OrderId">The order stored.</param>
    public sealed record StoreThenDeferMessage(string OrderId);

    /// <summary>A message whose handler sends an <see cref="OrderPlaced"/>, then defers the message, on its first delivery.</summary>
    /// <param name="OrderId">The order placed.</param>
    public sealed record SendThenDeferMessage(string OrderId);

    /// <summary>The message the handler of <see cref="SendThenDeferMessage"/> sends.</summary>
    /// <param name="OrderId">The order placed.</param>
    public sealed record OrderPlaced(string OrderId);

    /// <summary>A message sent after the one under test, whose handling bounds when anything sent before it has arrived.</summary>
    public sealed class MarkerMessage;

    /// <summary>An order a handler stores.</summary>
    public sealed class OrderDocument
    {
        /// <summary>Gets or sets the id.</summary>
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>A deferral the bus asked the timeout manager to hold back.</summary>
    /// <param name="Headers">The headers of the deferred message.</param>
    private sealed record Deferral(Dictionary<string, string> Headers);

    /// <summary>What the handlers and the outbox saw.</summary>
    private sealed class Recorder
    {
        private int _heldSends;

        public ConcurrentQueue<string?> DeferCounts { get; } = new();

        public bool DeferredBeforeThrowing { get; set; }

        public TaskCompletionSource MarkerHandled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int HeldSends => Volatile.Read(ref this._heldSends);

        public void RecordHeldSend()
        {
            Interlocked.Increment(ref this._heldSends);
        }
    }

    /// <summary>
    /// A timeout manager that records each message it is asked to defer and returns it as due at its next poll, so a deferral
    /// plays out in a moment rather than minutes.
    /// </summary>
    private sealed class RecordingTimeoutManager : ITimeoutManager
    {
        private readonly ConcurrentQueue<DueMessage> _pending = new();

        private int _handedBack;

        /// <summary>
        /// Gets or sets how many deferred messages are handed back in all, after which they are held for good.
        /// </summary>
        public int MaxHandBacks { get; set; } = 10;

        public ConcurrentQueue<Deferral> Deferrals { get; } = new();

        public Task Defer(DateTimeOffset approximateDueTime, Dictionary<string, string> headers, byte[] body)
        {
            this.Deferrals.Enqueue(new Deferral(new Dictionary<string, string>(headers)));
            this._pending.Enqueue(new DueMessage(headers, body, static () => Task.CompletedTask));

            return Task.CompletedTask;
        }

        public Task<DueMessagesResult> GetDueMessages()
        {
            var due = new List<DueMessage>();

            while (this._handedBack < this.MaxHandBacks && this._pending.TryDequeue(out var message))
            {
                due.Add(message);
                this._handedBack++;
            }

            return Task.FromResult(new DueMessagesResult(due, static () => Task.CompletedTask));
        }
    }

    /// <summary>The transaction the outbox is enlisted in, kept in the scope for the drain.</summary>
    private sealed class EnlistedTransaction
    {
        public ITransactionContext? Transaction { get; set; }

        public IDocumentSession? Session { get; set; }
    }

    /// <summary>Enlists the outbox: marks the transaction so the transport holds its sends, and records it for the drain.</summary>
    private sealed class OutboxEnlistment(EnlistedTransaction enlisted, IDocumentSession session) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            transactionContext.Items[OutboxTransport.HeldKey] = new ConcurrentQueue<TransportMessage>();
            enlisted.Transaction = transactionContext;
            enlisted.Session = session;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Holds a send made in a transaction the outbox is enlisted in, as Signature365's outbox transport does; sends anything else.</summary>
    private sealed class OutboxTransport(ITransport inner, Recorder recorder) : ITransport
    {
        public const string HeldKey = "test-outbox-held";

        public string Address => inner.Address;

        public void CreateQueue(string address)
        {
            inner.CreateQueue(address);
        }

        public Task Send(string destinationAddress, TransportMessage message, ITransactionContext context)
        {
            if (context.Items.TryGetValue(HeldKey, out var held))
            {
                ((ConcurrentQueue<TransportMessage>)held).Enqueue(message);
                recorder.RecordHeldSend();

                return Task.CompletedTask;
            }

            return inner.Send(destinationAddress, message, context);
        }

        public Task<TransportMessage> Receive(ITransactionContext context, CancellationToken cancellationToken)
        {
            return inner.Receive(context, cancellationToken);
        }
    }

    /// <summary>
    /// Writes the messages held in the scope's enlisted transaction as outbox rows during the save, and says whether there are any,
    /// so that a save with none needs no database.
    /// </summary>
    private sealed class DrainListener(string outboxTable) : DocumentSessionListenerBase, IOutboxDrainListener
    {
        public bool HasPendingMessages(IDocumentSession session)
        {
            return HeldFor(session) is { IsEmpty: false };
        }

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            if (HeldFor(session) is not { } held)
            {
                return Task.CompletedTask;
            }

            while (held.TryDequeue(out var message))
            {
                session.QueueSqlCommand(
                    $"insert into {outboxTable} (id, body) values (?, ?)",
                    message.Headers[Headers.MessageId],
                    message.Headers[Headers.Type]);
            }

            return Task.CompletedTask;
        }

        private static ConcurrentQueue<TransportMessage>? HeldFor(IDocumentSession session)
        {
            var enlisted = MartenScopeAccessor.Current?.GetService<EnlistedTransaction>();

            return enlisted is { Transaction: { } transaction }
                   && ReferenceEquals(enlisted.Session, session)
                   && transaction.Items.TryGetValue(OutboxTransport.HeldKey, out var held)
                ? (ConcurrentQueue<TransportMessage>)held
                : null;
        }
    }

    private sealed class DeferredMessageHandler(IBus bus, IMessageContext messageContext, Recorder recorder) : IHandleMessages<DeferredMessage>
    {
        public async Task Handle(DeferredMessage message)
        {
            var deferCount = messageContext.Headers.GetValueOrDefault(Headers.DeferCount);

            recorder.DeferCounts.Enqueue(deferCount);

            if (deferCount is null)
            {
                await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5));
            }
        }
    }

    private sealed class ForwardedMessageHandler(IBus bus) : IHandleMessages<ForwardedMessage>
    {
        public Task Handle(ForwardedMessage message)
        {
            return bus.Advanced.TransportMessage.Forward(ForwardQueue);
        }
    }

    private sealed class DeferThenThrowMessageHandler(IBus bus, Recorder recorder) : IHandleMessages<DeferThenThrowMessage>
    {
        public const string Failure = "The handler failed after deferring its message.";

        public async Task Handle(DeferThenThrowMessage message)
        {
            await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5));

            recorder.DeferredBeforeThrowing = true;

            throw new InvalidOperationException(Failure);
        }
    }

    private sealed class StoreThenDeferMessageHandler(IBus bus, IDocumentSession session) : IHandleMessages<StoreThenDeferMessage>
    {
        public async Task Handle(StoreThenDeferMessage message)
        {
            session.Store(new OrderDocument { Id = message.OrderId });

            await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5));
        }
    }

    private sealed class SendThenDeferMessageHandler(IBus bus, IMessageContext messageContext) : IHandleMessages<SendThenDeferMessage>
    {
        public async Task Handle(SendThenDeferMessage message)
        {
            if (messageContext.Headers.ContainsKey(Headers.DeferCount))
            {
                return;
            }

            // A fixed id, so the row can be asserted on
            await bus.Send(
                new OrderPlaced(message.OrderId),
                new Dictionary<string, string> { [Headers.MessageId] = message.OrderId + "-placed" });

            await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5));
        }
    }

    private sealed class MarkerMessageHandler(Recorder recorder) : IHandleMessages<MarkerMessage>
    {
        public Task Handle(MarkerMessage message)
        {
            recorder.MarkerHandled.TrySetResult();

            return Task.CompletedTask;
        }
    }
}
