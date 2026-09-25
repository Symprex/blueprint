using Blueprint.Marten.Tests.Postgres;
using Blueprint.Model;
using Blueprint.Rebus;
using FluentAssertions;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Transport;
using Rebus.Transport.InMem;

namespace Blueprint.Marten.Rebus.Tests;

/// <summary>
/// Proves the outbox shape the framework's seams are for, end to end, against a real PostgreSQL and a real Rebus bus: a
/// plain handler stores a document and sends a message; the send is held in the received message's transaction; and the
/// drain listener, added with <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>, writes it as an
/// outbox row during the save. The document and the row then commit together, or neither does.
/// </summary>
/// <remarks>
/// <para>
/// This is legacy's invariant, reached another way. Legacy queued the outbox rows on the session before it saved it, so
/// one database transaction held data and rows (Signature365 <c>8b40ecc961</c>,
/// <c>AzureAdOauthReturnCommandExecutorPipeline.cs:116-137</c>). Here the drain writes the rows during the save itself.
/// </para>
/// <para>
/// The transport decorator, enlistment and drain stand in for Signature365's: the decorator holds a send when the
/// transaction is enlisted, the enlistment records the transaction for the drain, and the drain reads it through
/// <see cref="MartenScopeAccessor.Current"/>, which <see cref="MartenUnitOfWork"/> sets for the save. These tests use the
/// plain-handler path, whose unit of work <see cref="BlueprintApiActivator"/> saves when the received transaction commits.
/// </para>
/// <para>
/// They need a database, so they are <see cref="PostgresFactAttribute"/>s: skipped where none is configured, as in CI.
/// </para>
/// </remarks>
public class OutboxDrainPostgresTests
{
    [PostgresFact]
    public async Task A_handler_that_stores_and_sends_commits_the_document_and_the_outbox_row_together()
    {
        await using var database = await PostgresTestStore.CreateAsync();

        var outcome = await RunAsync(database, new PlaceOrder("ORD-1", StoreOrder: true));

        outcome.HandlerRuns.Should().Be(1);
        (await database.ReadOutboxIdsAsync()).Should().Equal("ORD-1-placed");
        (await LoadOrderAsync(database, "ORD-1")).Should().NotBeNull();
        outcome.Network.GetCount("orders-placed").Should().Be(0, "the message went into the outbox, not to the transport");
    }

    [PostgresFact]
    public async Task A_handler_that_only_sends_still_has_its_message_written()
    {
        // Marten runs no listener for a session with nothing to save; MartenUnitOfWork makes it run the drain anyway.
        await using var database = await PostgresTestStore.CreateAsync();

        var outcome = await RunAsync(database, new PlaceOrder("ORD-1", StoreOrder: false));

        outcome.HandlerRuns.Should().Be(1);
        (await database.ReadOutboxIdsAsync()).Should().Equal("ORD-1-placed");
        (await LoadOrderAsync(database, "ORD-1")).Should().BeNull();
    }

    [PostgresFact]
    public async Task When_the_save_fails_then_neither_the_document_nor_the_outbox_row_is_written()
    {
        await using var database = await PostgresTestStore.CreateAsync();

        await using (var seed = database.Store.LightweightSession())
        {
            seed.Store(new OrderDocument { Id = "ORD-1", Status = "existing" });
            await seed.SaveChangesAsync();
        }

        // Inserting an order that exists fails the save's batch, which holds the drain's row as well.
        var outcome = await RunAsync(
            database,
            new PlaceOrder("ORD-1", StoreOrder: true, InsertOnly: true),
            static (network, _) => network.GetCount("error") == 1);

        outcome.HandlerRuns.Should().Be(1, "one delivery attempt is allowed");
        outcome.RowsDrained.Should().Be(1, "the drain queued the row in the save that failed, so its absence is a rollback");
        (await database.ReadOutboxIdsAsync()).Should().BeEmpty();
        (await LoadOrderAsync(database, "ORD-1"))!.Status.Should().Be("existing");
        outcome.Network.GetCount("orders-placed").Should().Be(0, "a message of a failed delivery is neither stored nor sent");

        var deadLettered = outcome.Network.GetNextOrNull("error");
        deadLettered!.Headers.Should().ContainKey(Headers.ErrorDetails)
            .WhoseValue.Should().Contain(nameof(DocumentAlreadyExistsException), "the save's failure is what the delivery failed on");
    }

    private static async Task<Outcome> RunAsync(
        PostgresTestStore database,
        PlaceOrder message,
        Func<InMemNetwork, Recorder, bool>? until = null)
    {
        until ??= static (_, recorder) => recorder.Completed;

        var recorder = new Recorder();
        database.Store.Options.AddBlueprintOutboxListener(new DrainListener(database.OutboxTable, recorder));

        var network = new InMemNetwork();
        network.CreateQueue("orders-placed");

        var services = new ServiceCollection();

        // An empty app, so the message takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton(recorder);
        services.AddBlueprintMarten();
        services.AddScoped<IDocumentSession>(_ => database.Store.LightweightSession());
        services.AddScoped<EnlistedTransaction>();
        services.AddScoped<IRebusTransactionEnlistment, OutboxEnlistment>();
        services.AddScoped<IHandleMessages<PlaceOrder>, PlaceOrderHandler>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using (var bus = Configure.With(new BlueprintApiActivator(provider))
                   .Transport(t => t.UseInMemoryTransport(network, "orders"))
                   .Routing(r => r.TypeBased().Map<OrderPlaced>("orders-placed"))
                   .Options(static o =>
                   {
                       o.RetryStrategy(maxDeliveryAttempts: 1);
                       o.Decorate<ITransport>(static c => new OutboxTransport(c.Get<ITransport>()));
                   })
                   .Start())
        {
            recorder.Bus = bus;
            await bus.SendLocal(message);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!until(network, recorder) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            until(network, recorder).Should().BeTrue("the delivery should have finished within the time allowed");
        }

        return new Outcome(network, recorder.HandlerRuns, recorder.RowsDrained);
    }

    private static async Task<OrderDocument?> LoadOrderAsync(PostgresTestStore database, string id)
    {
        await using var query = database.Store.QuerySession();

        return await query.LoadAsync<OrderDocument>(id);
    }

    /// <summary>A message the handler handles.</summary>
    /// <param name="OrderId">The order placed.</param>
    /// <param name="StoreOrder">Whether the handler stores the order, or only sends.</param>
    /// <param name="InsertOnly">Whether the handler inserts the order, failing if it exists, rather than upserting it.</param>
    public sealed record PlaceOrder(string OrderId, bool StoreOrder, bool InsertOnly = false);

    /// <summary>The message the handler sends.</summary>
    /// <param name="OrderId">The order placed.</param>
    public sealed record OrderPlaced(string OrderId);

    /// <summary>An order the handler stores.</summary>
    public sealed class OrderDocument
    {
        /// <summary>Gets or sets the id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the status.</summary>
        public string Status { get; set; } = string.Empty;
    }

    private sealed record Outcome(InMemNetwork Network, int HandlerRuns, int RowsDrained);

    private sealed class Recorder
    {
        private int _handlerRuns;
        private int _rowsDrained;
        private int _completed;

        public IBus? Bus { get; set; }

        public int HandlerRuns => Volatile.Read(ref this._handlerRuns);

        public bool Completed => Volatile.Read(ref this._completed) == 1;

        public int RowsDrained => Volatile.Read(ref this._rowsDrained);

        public void RecordRowDrained()
        {
            Interlocked.Increment(ref this._rowsDrained);
        }

        public void RecordHandlerRun()
        {
            Interlocked.Increment(ref this._handlerRuns);
        }

        public void RecordCompleted()
        {
            Interlocked.Exchange(ref this._completed, 1);
        }
    }

    /// <summary>The transaction an outbox is enlisted in, kept in the scope for the drain.</summary>
    private sealed class EnlistedTransaction
    {
        public ITransactionContext? Transaction { get; set; }
    }

    /// <summary>
    /// Enlists the outbox: marks the transaction so the transport holds its sends, and records it for the drain. It
    /// also records how the delivery ended, for the tests.
    /// </summary>
    private sealed class OutboxEnlistment(EnlistedTransaction enlisted, Recorder recorder) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            transactionContext.Items[OutboxTransport.HeldKey] = new List<TransportMessage>();
            enlisted.Transaction = transactionContext;

            transactionContext.OnDisposed(_ => recorder.RecordCompleted());

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Holds a send in an enlisted transaction instead of sending it; sends anything else.</summary>
    private sealed class OutboxTransport(ITransport inner) : ITransport
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
                ((List<TransportMessage>)held).Add(message);

                return Task.CompletedTask;
            }

            return inner.Send(destinationAddress, message, context);
        }

        public Task<TransportMessage> Receive(ITransactionContext context, CancellationToken cancellationToken)
        {
            return inner.Receive(context, cancellationToken);
        }
    }

    /// <summary>Writes the messages held in the enlisted transaction as outbox rows, during the save.</summary>
    private sealed class DrainListener(string outboxTable, Recorder recorder) : DocumentSessionListenerBase
    {
        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            var transaction = MartenScopeAccessor.Current?.GetService<EnlistedTransaction>()?.Transaction;

            if (transaction is null || !transaction.Items.TryGetValue(OutboxTransport.HeldKey, out var held))
            {
                return Task.CompletedTask;
            }

            var messages = (List<TransportMessage>)held;

            foreach (var message in messages)
            {
                session.QueueSqlCommand(
                    $"insert into {outboxTable} (id, body) values (?, ?)",
                    message.Headers[Headers.MessageId],
                    message.Headers[Headers.Type]);

                recorder.RecordRowDrained();
            }

            // Drained, so a second save writes nothing twice.
            messages.Clear();

            return Task.CompletedTask;
        }
    }

    private sealed class PlaceOrderHandler(IDocumentSession session, Recorder recorder) : IHandleMessages<PlaceOrder>
    {
        public async Task Handle(PlaceOrder message)
        {
            recorder.RecordHandlerRun();

            if (message.StoreOrder)
            {
                var order = new OrderDocument { Id = message.OrderId, Status = "placed" };

                if (message.InsertOnly)
                {
                    session.Insert(order);
                }
                else
                {
                    session.Store(order);
                }
            }

            // A fixed id, so the row can be asserted on.
            await recorder.Bus!.Send(
                new OrderPlaced(message.OrderId),
                new Dictionary<string, string> { [Headers.MessageId] = message.OrderId + "-placed" });
        }
    }
}
