using Blueprint.Model;
using Blueprint.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Pipeline;
using Rebus.Retry.Simple;
using Rebus.Transport;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Pins what Rebus 8 makes ambient while its worker runs a received message's commit callbacks, which is when
/// <see cref="BlueprintApiActivator"/> saves a plain handler's unit of work.
/// </summary>
/// <remarks>
/// A consumer's outbox drain runs inside that save, as a Marten session listener, and must find the transaction the
/// handlers sent their messages in. If that transaction is <see cref="AmbientTransactionContext.Current"/> there, the
/// drain can read it directly; if not, it needs the context the enlistment seam was given. And a domain event published
/// during that save may be the transaction's first send, so a message first sent from a commit callback must still be
/// sent.
/// </remarks>
public class RebusCommitAmbientContextTests
{
    [Fact]
    public async Task While_the_worker_runs_commit_callbacks_the_received_messages_transaction_is_ambient()
    {
        var observations = new Observations();
        var network = new InMemNetwork();

        using var provider = BuildProvider(observations);
        using var bus = Configure.With(new BlueprintApiActivator(provider))
            .Transport(t => t.UseInMemoryTransport(network, "input"))
            .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 1))
            .Start();

        await bus.SendLocal(new ProbeMessage());

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!observations.Saved.IsCompleted)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the unit of work should be saved when the message commits");
            await Task.Delay(20);
        }

        observations.HandlerTransaction.Should().NotBeNull();
        observations.AmbientDuringSave.Should().BeSameAs(
            observations.HandlerTransaction,
            "the received message's transaction context is ambient while its commit callbacks run");
    }

    [Fact]
    public async Task A_message_first_sent_while_the_received_messages_commit_callbacks_run_is_still_sent()
    {
        // A plain handler's unit of work is saved from the received transaction's first commit callback, and a domain
        // event published during that save is, when the handler sent nothing, the transaction's first send: the
        // transport registers its send callback while the commit callbacks are already running.
        var followUp = new FollowUpObservations();
        var network = new InMemNetwork();

        var services = new ServiceCollection();
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton(followUp);
        services.AddScoped<IUnitOfWork, SendingUnitOfWork>();
        services.AddScoped<IHandleMessages<ProbeMessage>, SilentHandler>();
        services.AddScoped<IHandleMessages<FollowUpMessage>, FollowUpHandler>();

        using var provider = services.BuildServiceProvider();
        using var bus = Configure.With(new BlueprintApiActivator(provider))
            .Transport(t => t.UseInMemoryTransport(network, "input"))
            .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 1))
            .Start();
        followUp.Bus = bus;

        await bus.SendLocal(new ProbeMessage());

        var received = await Task.WhenAny(followUp.Received, Task.Delay(TimeSpan.FromSeconds(10)));

        followUp.SentDuringSave.Should().BeTrue("the save of the received message should have sent the follow-up");
        followUp.SentInReceivedTransaction.Should().BeTrue(
            "the follow-up must go into the received message's transaction, not straight out, for this to prove anything");
        received.Should().BeSameAs(followUp.Received, "a message sent from a commit callback must still leave the bus");
    }

    private static ServiceProvider BuildProvider(Observations observations)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton(observations);
        services.AddScoped<IUnitOfWork, ObservingUnitOfWork>();
        services.AddScoped<IHandleMessages<ProbeMessage>, ObservingHandler>();

        return services.BuildServiceProvider();
    }

    /// <summary>A message the real bus serialises, so it must be public.</summary>
    public sealed class ProbeMessage;

    /// <summary>A message sent from inside the save, which the real bus serialises, so it must be public.</summary>
    public sealed class FollowUpMessage;

    private sealed class FollowUpObservations
    {
        private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sent;

        public global::Rebus.Bus.IBus? Bus { get; set; }

        public bool SentDuringSave => Volatile.Read(ref this._sent) == 1;

        public bool SentInReceivedTransaction { get; set; }

        public Task Received => this._received.Task;

        public bool TryClaimSend()
        {
            return Interlocked.Exchange(ref this._sent, 1) == 0;
        }

        public void RecordReceived()
        {
            this._received.TrySetResult();
        }
    }

    private sealed class SilentHandler : IHandleMessages<ProbeMessage>
    {
        public Task Handle(ProbeMessage message)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FollowUpHandler(FollowUpObservations observations) : IHandleMessages<FollowUpMessage>
    {
        public Task Handle(FollowUpMessage message)
        {
            observations.RecordReceived();

            return Task.CompletedTask;
        }
    }

    private sealed class SendingUnitOfWork(FollowUpObservations observations) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Only the probe message's save sends, so the follow-up's own save does not start a chain.
            if (MessageContext.Current is not { Message.Body: ProbeMessage } messageContext || !observations.TryClaimSend())
            {
                return Task.CompletedTask;
            }

            observations.SentInReceivedTransaction = AmbientTransactionContext.Current is { } ambient
                                                     && ReferenceEquals(ambient, messageContext.TransactionContext);

            return observations.Bus!.SendLocal(new FollowUpMessage());
        }
    }

    private sealed class Observations
    {
        private readonly TaskCompletionSource _saved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ITransactionContext? HandlerTransaction { get; set; }

        public ITransactionContext? AmbientDuringSave { get; private set; }

        public Task Saved => this._saved.Task;

        public void RecordSave()
        {
            this.AmbientDuringSave = AmbientTransactionContext.Current;
            this._saved.TrySetResult();
        }
    }

    private sealed class ObservingHandler(Observations observations) : IHandleMessages<ProbeMessage>
    {
        public Task Handle(ProbeMessage message)
        {
            observations.HandlerTransaction = MessageContext.Current.TransactionContext;

            return Task.CompletedTask;
        }
    }

    private sealed class ObservingUnitOfWork(Observations observations) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            observations.RecordSave();

            return Task.CompletedTask;
        }
    }
}
