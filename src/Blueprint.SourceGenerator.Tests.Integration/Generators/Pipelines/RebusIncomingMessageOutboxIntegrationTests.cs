using System.Collections.Concurrent;
using Blueprint.Marten;
using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Rebus;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Messages;
using Rebus.Retry.Simple;
using Rebus.Timeouts;
using Rebus.Transport;
using Rebus.Transport.InMem;
using Weasel.Core;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the handler of an operation delivered as a Rebus message, whose pipeline has an outbox enlisted in its own Rebus
/// transaction, can defer that message while the database is down: the deferred copy is sent in the message's own transaction,
/// outside the outbox, so the pipeline's save has nothing to write and needs no database.
/// </summary>
/// <remarks>
/// <para>
/// The outbox stands in for Signature365's: a transport decorator that holds a send made in a transaction the outbox is enlisted
/// in, an <see cref="IRebusTransactionEnlistment"/> that the pipeline calls for its own transaction, and a drain, added with
/// <see cref="Blueprint.Marten.MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>, that is an <see cref="IOutboxDrainListener"/>, so a
/// save with nothing held needs no database. The Marten store points at a port nothing listens on.
/// </para>
/// <para>
/// The plain-handler counterpart, where the outbox is enlisted in the message's own transaction instead, is
/// <c>Blueprint.Marten.Rebus.Tests.PlainHandlerDeferralOutboxTests</c>.
/// </para>
/// </remarks>
public sealed class RebusIncomingMessageOutboxIntegrationTests : IDisposable
{
    private const string InputQueue = "rebus-incoming-message-outbox-tests";
    private const string ErrorQueue = "error";

    private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(30);

    private readonly InMemNetwork _network = new();
    private readonly RecordingTimeoutManager _timeoutManager = new();
    private readonly RebusOutboxDeferralProbe _probe = new();
    private readonly DocumentStore _store = CreateUnreachableStore();

    private ServiceProvider? _services;

    public void Dispose()
    {
        // Disposing the bus stops its workers
        this._services?.Dispose();
        this._store.Dispose();
    }

    [Fact]
    public async Task When_an_operation_whose_pipeline_enlists_an_outbox_defers_its_message_while_the_database_is_down_then_the_copy_is_deferred()
    {
        // Arrange
        this.StartBus();
        var messageId = Guid.NewGuid().ToString();

        // Act
        await this._services!.GetRequiredService<IBus>().SendLocal(new OutboxDeferringRebusCommand(), new Dictionary<string, string> { [Headers.MessageId] = messageId });

        await this.WaitForAsync(() => this._probe.Deliveries >= 2);

        // Assert
        var errorDetails = this._network.GetNextOrNull(ErrorQueue) is { } deadLettered
            ? deadLettered.Headers.GetValueOrDefault(Headers.ErrorDetails, "(no error details)")
            : null;
        errorDetails.Should().BeNull("deferring the message must not need the database it is deferring for");

        this._probe.Enlistments.Should().BeGreaterThan(0, "the pipeline must have enlisted the outbox for this to prove anything");
        this._probe.Deliveries.Should().Be(2, "the message is handled, deferred, then handled again once it is due");

        var deferral = this._timeoutManager.Deferrals.Should().ContainSingle().Subject;
        deferral[Headers.MessageId].Should().Be(messageId);
        deferral[Headers.DeferCount].Should().Be("1");
        this._probe.HeldSends.Should().Be(0, "the deferred copy must not go through the outbox");
    }

    private static DocumentStore CreateUnreachableStore()
    {
        var options = new StoreOptions();
        options.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1;Pooling=false");
        options.AutoCreateSchemaObjects = AutoCreate.None;
        options.AddBlueprintOutboxListener(new DrainListener());

        return new DocumentStore(options);
    }

    private void StartBus()
    {
        var services = new ServiceCollection().AddLogging();

        services.AddSingleton(this._probe);
        services.AddScoped<IDocumentSession>(_ => this._store.LightweightSession());
        services.AddScoped<IUnitOfWork, MartenUnitOfWork>();
        services.AddScoped<EnlistedTransaction>();
        services.AddScoped<IRebusTransactionEnlistment, OutboxEnlistment>();

        services.AddBlueprintRebusConsumer(static _ => { });
        services.AddBlueprintRebus((_, configurer) => configurer
            .Transport(t => t.UseInMemoryTransport(this._network, InputQueue))
            .Timeouts(t => t.Register(_ => this._timeoutManager))
            .Options(o =>
            {
                o.RetryStrategy(maxDeliveryAttempts: 1);
                o.SetNumberOfWorkers(1);
                o.SetMaxParallelism(1);
                o.Decorate<ITransport>(c => new OutboxTransport(c.Get<ITransport>(), this._probe));
            }));
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        this._services = services.BuildServiceProvider();

        // Builds the pipelines up front, so that a failure to do so is reported as such rather than as a timeout
        this._services.GetRequiredService<IntegrationTestBlueprintAppExecutor>();
        this._services.StartRebus();
    }

    private async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _waitLimit;

        while (!condition() && this._network.GetCount(ErrorQueue) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// A timeout manager that records the headers of each message it is asked to defer and returns it as due at its next poll.
    /// </summary>
    private sealed class RecordingTimeoutManager : ITimeoutManager
    {
        private readonly ConcurrentQueue<DueMessage> _pending = new();

        public ConcurrentQueue<Dictionary<string, string>> Deferrals { get; } = new();

        public Task Defer(DateTimeOffset approximateDueTime, Dictionary<string, string> headers, byte[] body)
        {
            this.Deferrals.Enqueue(new Dictionary<string, string>(headers));
            this._pending.Enqueue(new DueMessage(headers, body, static () => Task.CompletedTask));

            return Task.CompletedTask;
        }

        public Task<DueMessagesResult> GetDueMessages()
        {
            var due = new List<DueMessage>();

            while (this._pending.TryDequeue(out var message))
            {
                due.Add(message);
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
    private sealed class OutboxEnlistment(EnlistedTransaction enlisted, IDocumentSession session, RebusOutboxDeferralProbe probe) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            transactionContext.Items[OutboxTransport.HeldKey] = new ConcurrentQueue<TransportMessage>();
            enlisted.Transaction = transactionContext;
            enlisted.Session = session;
            probe.RecordEnlistment();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Holds a send made in a transaction the outbox is enlisted in, as Signature365's outbox transport does; sends anything else.</summary>
    private sealed class OutboxTransport(ITransport inner, RebusOutboxDeferralProbe probe) : ITransport
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
                probe.RecordHeldSend();

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
    private sealed class DrainListener : DocumentSessionListenerBase, IOutboxDrainListener
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
                session.QueueSqlCommand("insert into outbox (id) values (?)", message.Headers[Headers.MessageId]);
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
}

/// <summary>Records what the handler of <see cref="OutboxDeferringRebusCommand"/> and the outbox saw.</summary>
public sealed class RebusOutboxDeferralProbe
{
    private int _deliveries;
    private int _enlistments;
    private int _heldSends;

    /// <summary>Gets how many times the handler has been given its message.</summary>
    public int Deliveries => Volatile.Read(ref this._deliveries);

    /// <summary>Gets how many times the outbox was enlisted.</summary>
    public int Enlistments => Volatile.Read(ref this._enlistments);

    /// <summary>Gets how many sends the outbox held.</summary>
    public int HeldSends => Volatile.Read(ref this._heldSends);

    /// <summary>Records a delivery and returns how many there have been.</summary>
    /// <returns>The number of this delivery, from 1.</returns>
    public int RecordDelivery()
    {
        return Interlocked.Increment(ref this._deliveries);
    }

    /// <summary>Records that the outbox was enlisted.</summary>
    public void RecordEnlistment()
    {
        Interlocked.Increment(ref this._enlistments);
    }

    /// <summary>Records that the outbox held a send.</summary>
    public void RecordHeldSend()
    {
        Interlocked.Increment(ref this._heldSends);
    }
}

/// <summary>A message whose handler defers it on its first delivery, inside a pipeline that enlists an outbox.</summary>
public partial class OutboxDeferringRebusCommand : ICommand
{
    public static Task HandleAsync(OutboxDeferringRebusCommand operation, IBus bus, RebusOutboxDeferralProbe probe)
    {
        return probe.RecordDelivery() == 1
            ? bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5))
            : Task.CompletedTask;
    }
}
