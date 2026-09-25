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
/// <para>
/// A consumer's outbox drain runs inside that save, as a Marten session listener, and must find the transaction the
/// handlers sent their messages in. If that transaction is <see cref="AmbientTransactionContext.Current"/> there, the
/// drain can read it directly; if not, it needs the context the enlistment seam was given. And a domain event published
/// during that save may be the transaction's first send, so a message first sent from a commit callback must still be
/// sent.
/// </para>
/// <para>
/// A send joins the transaction's outgoing messages, which Rebus 8.6's transport sends from one commit callback of its
/// own. The transport registers that callback only with the transaction's first send (<c>AbstractRebusTransport.Send</c>,
/// inside its <c>GetOrAdd("outgoing-messages")</c> factory). So a message sent from a commit callback is sent only if that
/// callback is yet to run: never when the callback has already sent the queue, on either kind of transaction. When the
/// send is the transaction's first, it depends on whether the transaction runs a commit callback registered while
/// its callbacks run. A received message's transaction does. The <see cref="RebusTransactionScope"/> a generated pipeline
/// opens does not, so there a message sent from a commit callback is silently never sent, unless the callback was
/// registered before the scope's first send.
/// </para>
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

    [Fact]
    public async Task A_message_sent_from_a_RebusTransactionScopes_commit_callback_after_its_send_callback_ran_is_not_sent()
    {
        // The scope's first send registered the transport's callback before this one, so that callback sends the queue
        // first; the follow-up then joins a queue nothing will send again, and never leaves, with no error.
        var probe = new ScopeProbeObservations();
        var network = new InMemNetwork();

        var services = new ServiceCollection();
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton(probe);
        services.AddScoped<IHandleMessages<FollowUpMessage>, ScopeProbeFollowUpHandler>();
        services.AddScoped<IHandleMessages<MarkerMessage>, ScopeProbeMarkerHandler>();
        services.AddScoped<IHandleMessages<InScopeMessage>, ScopeProbeInScopeHandler>();

        using var provider = services.BuildServiceProvider();
        using var bus = Configure.With(new BlueprintApiActivator(provider))
            .Transport(t => t.UseInMemoryTransport(network, "input"))
            .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 1))
            .Start();

        using (var scope = new RebusTransactionScope())
        {
            // Sent in the scope, so the scope has a send callback of its own: proves the scope does send.
            await bus.SendLocal(new InScopeMessage());

            scope.TransactionContext.OnCommit(async _ =>
            {
                try
                {
                    await bus.SendLocal(new FollowUpMessage());
                    probe.CallbackSent = true;
                }
                catch (Exception exception)
                {
                    probe.CallbackError = exception;
                }
            });

            await scope.CompleteAsync();
        }

        // Sent after the scope, outside any transaction: once it has arrived, a follow-up that was sent would have too.
        await bus.SendLocal(new MarkerMessage());
        await probe.WaitForAsync<MarkerMessage>(TimeSpan.FromSeconds(10));
        await probe.WaitForAsync<InScopeMessage>(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        probe.CallbackError.Should().BeNull("the send from the commit callback is not refused");
        probe.CallbackSent.Should().BeTrue("the commit callback ran and its send returned");
        probe.HasReceived<FollowUpMessage>().Should().BeFalse(
            "the transport had already sent the transaction's queue, and registers no second callback for a later send");
    }

    [Fact]
    public async Task A_message_first_sent_from_a_RebusTransactionScopes_commit_callback_is_not_sent()
    {
        // Nothing is sent in the scope before the callback, so the follow-up is the transaction's first send, and it
        // registers the transport's callback while the scope runs the callbacks it holds. RebusTransactionScope runs no
        // others, so the follow-up never leaves, with no error.
        var probe = new ScopeProbeObservations();
        using var provider = BuildScopeProbeProvider(probe);
        using var bus = StartBus(provider, new InMemNetwork());

        using (var scope = new RebusTransactionScope())
        {
            scope.TransactionContext.OnCommit(async _ => await SendFromCallbackAsync(bus, probe));

            await scope.CompleteAsync();
        }

        await WaitPastMarkerAsync(bus, probe);

        probe.CallbackError.Should().BeNull("the send from the commit callback is not refused");
        probe.CallbackSent.Should().BeTrue("the commit callback ran and its send returned");
        probe.HasReceived<FollowUpMessage>().Should().BeFalse(
            "RebusTransactionScope ran only the commit callbacks it had when it completed, so the follow-up's send callback never ran");
    }

    [Fact]
    public async Task A_message_sent_from_a_commit_callback_registered_before_the_scopes_first_send_is_sent()
    {
        // The boundary of the two cases above: the transport's callback, registered by the in-scope send after this one,
        // runs after it and sends the follow-up with the in-scope message.
        var probe = new ScopeProbeObservations();
        using var provider = BuildScopeProbeProvider(probe);
        using var bus = StartBus(provider, new InMemNetwork());

        using (var scope = new RebusTransactionScope())
        {
            scope.TransactionContext.OnCommit(async _ => await SendFromCallbackAsync(bus, probe));
            await bus.SendLocal(new InScopeMessage());

            await scope.CompleteAsync();
        }

        await probe.WaitForAsync<InScopeMessage>(TimeSpan.FromSeconds(10));
        await probe.WaitForAsync<FollowUpMessage>(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_message_sent_from_a_received_messages_commit_callback_after_its_send_callback_ran_is_not_sent()
    {
        // The same queue rule on a received message's transaction: the handler's send registers the transport's callback,
        // the handler then registers one that sends, and that one runs after the queue has been sent.
        var probe = new ScopeProbeObservations();
        var services = new ServiceCollection();
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton(probe);
        services.AddScoped<IHandleMessages<ProbeMessage>, SendThenRegisterSendingCallbackHandler>();
        services.AddScoped<IHandleMessages<FollowUpMessage>, ScopeProbeFollowUpHandler>();
        services.AddScoped<IHandleMessages<MarkerMessage>, ScopeProbeMarkerHandler>();
        services.AddScoped<IHandleMessages<InScopeMessage>, ScopeProbeInScopeHandler>();

        using var provider = services.BuildServiceProvider();
        using var bus = StartBus(provider, new InMemNetwork());
        probe.Bus = bus;

        await bus.SendLocal(new ProbeMessage());
        await probe.WaitForAsync<InScopeMessage>(TimeSpan.FromSeconds(10));
        await WaitPastMarkerAsync(bus, probe);

        probe.CallbackError.Should().BeNull("the send from the commit callback is not refused");
        probe.CallbackSent.Should().BeTrue("the commit callback ran and its send returned");
        probe.HasReceived<FollowUpMessage>().Should().BeFalse(
            "the transport had already sent the transaction's queue, and registers no second callback for a later send");
    }

    private static ServiceProvider BuildScopeProbeProvider(ScopeProbeObservations probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton(new BlueprintRebusConfiguration());
        services.AddSingleton(probe);
        services.AddScoped<IHandleMessages<FollowUpMessage>, ScopeProbeFollowUpHandler>();
        services.AddScoped<IHandleMessages<MarkerMessage>, ScopeProbeMarkerHandler>();
        services.AddScoped<IHandleMessages<InScopeMessage>, ScopeProbeInScopeHandler>();

        return services.BuildServiceProvider();
    }

    private static global::Rebus.Bus.IBus StartBus(ServiceProvider provider, InMemNetwork network)
    {
        return Configure.With(new BlueprintApiActivator(provider))
            .Transport(t => t.UseInMemoryTransport(network, "input"))
            .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 1))
            .Start();
    }

    private static async Task SendFromCallbackAsync(global::Rebus.Bus.IBus bus, ScopeProbeObservations probe)
    {
        try
        {
            await bus.SendLocal(new FollowUpMessage());
            probe.CallbackSent = true;
        }
        catch (Exception exception)
        {
            probe.CallbackError = exception;
        }
    }

    /// <summary>
    /// Sends a marker outside any transaction and waits for it, and a little more: once it has arrived, a follow-up that
    /// was sent before it would have too.
    /// </summary>
    private static async Task WaitPastMarkerAsync(global::Rebus.Bus.IBus bus, ScopeProbeObservations probe)
    {
        await bus.SendLocal(new MarkerMessage());
        await probe.WaitForAsync<MarkerMessage>(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
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

    /// <summary>A message sent after the scope, to bound how long a message that was sent takes to arrive.</summary>
    public sealed class MarkerMessage;

    /// <summary>A message sent inside the scope before it completes.</summary>
    public sealed class InScopeMessage;

    private sealed class ScopeProbeObservations
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, TaskCompletionSource> _received = new();

        public global::Rebus.Bus.IBus? Bus { get; set; }

        public bool CallbackSent { get; set; }

        public Exception? CallbackError { get; set; }

        public void RecordReceived(Type messageType)
        {
            this.Arrival(messageType).TrySetResult();
        }

        public bool HasReceived<T>()
        {
            return this.Arrival(typeof(T)).Task.IsCompleted;
        }

        public async Task WaitForAsync<T>(TimeSpan timeout)
        {
            var arrival = this.Arrival(typeof(T)).Task;
            (await Task.WhenAny(arrival, Task.Delay(timeout))).Should().BeSameAs(arrival, "{0} should arrive", typeof(T).Name);
        }

        private TaskCompletionSource Arrival(Type messageType)
        {
            return this._received.GetOrAdd(messageType, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    // Not one open generic handler: AddBlueprintRebusConsumer's assembly scan (RebusServiceCollectionExtensionsTests)
    // cannot register an open generic implementation.
    private sealed class ScopeProbeFollowUpHandler(ScopeProbeObservations observations) : IHandleMessages<FollowUpMessage>
    {
        public Task Handle(FollowUpMessage message)
        {
            observations.RecordReceived(typeof(FollowUpMessage));

            return Task.CompletedTask;
        }
    }

    private sealed class ScopeProbeMarkerHandler(ScopeProbeObservations observations) : IHandleMessages<MarkerMessage>
    {
        public Task Handle(MarkerMessage message)
        {
            observations.RecordReceived(typeof(MarkerMessage));

            return Task.CompletedTask;
        }
    }

    private sealed class ScopeProbeInScopeHandler(ScopeProbeObservations observations) : IHandleMessages<InScopeMessage>
    {
        public Task Handle(InScopeMessage message)
        {
            observations.RecordReceived(typeof(InScopeMessage));

            return Task.CompletedTask;
        }
    }

    private sealed class SendThenRegisterSendingCallbackHandler(ScopeProbeObservations observations) : IHandleMessages<ProbeMessage>
    {
        public async Task Handle(ProbeMessage message)
        {
            await observations.Bus!.SendLocal(new InScopeMessage());

            MessageContext.Current.TransactionContext.OnCommit(async _ => await SendFromCallbackAsync(observations.Bus!, observations));
        }
    }

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
