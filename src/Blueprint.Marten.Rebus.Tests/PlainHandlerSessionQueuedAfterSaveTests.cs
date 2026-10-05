using Blueprint.Model;
using Blueprint.Rebus;
using FluentAssertions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Retry.Simple;
using Rebus.Transport;
using Rebus.Transport.InMem;
using Weasel.Core;
using Xunit;

namespace Blueprint.Marten.Rebus.Tests;

/// <summary>
/// Proves that when an enlisted outbox queues work on a plain Rebus handler's Marten session after
/// <see cref="BlueprintApiActivator"/> has saved it, completing the received message's transaction fails, rather than the
/// work being silently dropped.
/// </summary>
/// <remarks>
/// <para>
/// The activator saves the handler's unit of work from the transaction's first commit callback, so a commit callback an
/// outbox registers when a handler first sends — legacy's outbox transport queued each message's row on the session from
/// one — runs after that save, on a session that will not save again.
/// </para>
/// <para>
/// No database is needed: the session has nothing to save when the activator saves it, so Marten never opens a connection.
/// </para>
/// </remarks>
public class PlainHandlerSessionQueuedAfterSaveTests
{
    [Fact]
    public async Task When_an_enlisted_outbox_queues_on_the_session_after_the_save_then_completing_the_transaction_fails()
    {
        using var store = CreateUnreachableStore();
        await using var provider = BuildProvider(store);
        var activator = new BlueprintApiActivator(provider);

        using var transactionScope = new RebusTransactionScope();

        var handlers = await activator.GetHandlers(new SendingMessage(), transactionScope.TransactionContext);
        await handlers.Should().ContainSingle().Subject.Handle(new SendingMessage());

        var complete = () => transactionScope.CompleteAsync();

        await complete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*after it was saved*");
    }

    [Fact]
    public async Task On_a_real_bus_the_failed_completion_is_not_retried_and_the_message_does_not_reach_the_error_queue()
    {
        // Pins what "loud" means for a received message: the check runs after every commit callback, among the ack
        // callbacks, after the transport's own, so the delivery is already acknowledged when it fails.
        using var store = CreateUnreachableStore();
        await using var provider = BuildProvider(store);
        var network = new InMemNetwork();

        using (var bus = Configure.With(new BlueprintApiActivator(provider))
                   .Transport(t => t.UseInMemoryTransport(network, "input"))
                   .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 2))
                   .Start())
        {
            await bus.SendLocal(new SendingMessage());

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (provider.GetRequiredService<HandleCounter>().Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            // Long enough for a redelivery, had there been one.
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        provider.GetRequiredService<HandleCounter>().Count.Should().Be(1, "an acknowledged delivery is not redelivered");
        network.GetCount("input").Should().Be(0);
        network.GetCount("error").Should().Be(0, "the delivery did not fail; its completion did, after the acknowledgement");
    }

    private static DocumentStore CreateUnreachableStore()
    {
        var options = new StoreOptions();
        options.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
        options.AutoCreateSchemaObjects = AutoCreate.None;

        return new DocumentStore(options);
    }

    private static ServiceProvider BuildProvider(DocumentStore store)
    {
        var services = new ServiceCollection();

        // An empty app, so the message takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton<HandleCounter>();
        services.AddBlueprintMarten();
        services.AddScoped<IDocumentSession>(_ => store.LightweightSession());
        services.AddScoped<IRebusTransactionEnlistment, LegacyStyleOutboxEnlistment>();
        services.AddScoped<IHandleMessages<SendingMessage>, SendingHandler>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>A message whose handler sends another and writes nothing.</summary>
    public sealed class SendingMessage;

    /// <summary>Counts the handler's invocations.</summary>
    private sealed class HandleCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref this._count);

        public void Increment()
        {
            Interlocked.Increment(ref this._count);
        }
    }

    /// <summary>As legacy's <c>UseMartenSessionAsOutbox</c> did: puts the session into the transaction.</summary>
    private sealed class LegacyStyleOutboxEnlistment(IDocumentSession session) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            transactionContext.Items["current-outbox-connection"] = session;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Sends as legacy's outbox transport did: queues the row on the enlisted session when the transaction commits.</summary>
    private sealed class SendingHandler(HandleCounter counter) : IHandleMessages<SendingMessage>
    {
        public Task Handle(SendingMessage message)
        {
            counter.Increment();

            var transaction = AmbientTransactionContext.Current!;
            var session = (IDocumentSession)transaction.Items["current-outbox-connection"];

            transaction.OnCommit(_ =>
            {
                session.QueueSqlCommand("insert into outbox (id) values (?)", "MSG-1");

                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        }
    }
}
