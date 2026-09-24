using Blueprint.Model;
using Blueprint.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Retry.Simple;
using Rebus.Transport;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="BlueprintApiActivator"/> saves the <see cref="IUnitOfWork"/> of a plain (non-operation)
/// <see cref="IHandleMessages{TMessage}"/>'s scope when, and only when, Rebus commits the message's transaction.
/// </summary>
/// <remarks>
/// Legacy Blueprint ran every subscribed message as an operation whose pipeline saved the Marten session after the
/// handler, so Signature365's plain handlers write through an injected session and never save it themselves. Without
/// this save their writes are silently discarded.
/// </remarks>
public class BlueprintApiActivatorUnitOfWorkTests
{
    [Fact]
    public async Task GetHandlers_for_plain_handler_saves_its_scopes_unit_of_work_when_the_transaction_commits()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, UnitOfWorkUsingHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<UnitOfWorkUsingHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage());

        handler.UnitOfWork.SaveCount.Should().Be(0, "nothing may be saved before Rebus decides the message succeeded");

        await transactionScope.CompleteAsync();

        handler.UnitOfWork.SaveCount.Should().Be(1, "the unit of work the handler wrote through is saved on commit");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_saves_before_commit_callbacks_the_handler_registered()
    {
        // The transport registers its send-outgoing-messages callback with OnCommit the first time a handler sends,
        // so a save that ran after it would publish messages about writes that could still fail to persist.
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, CommitCallbackRegisteringHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<CommitCallbackRegisteringHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage());
        await transactionScope.CompleteAsync();

        handler.Log.Entries.Should().Equal("save", "send");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_when_handler_throws_does_not_save()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, ThrowingHandler>());
        var activator = new BlueprintApiActivator(provider);
        var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<ThrowingHandler>(activator, transactionScope.TransactionContext);

        var handle = () => handler.Handle(new PlainMessage());
        await handle.Should().ThrowExactlyAsync<InvalidOperationException>();

        // As Rebus's retry step records for a failed delivery: no commit, and nack so it is redelivered. Not completed
        // through RebusTransactionScope.CompleteAsync, which always commits; the bus test below drives the real
        // worker's completion of a failed delivery.
        transactionScope.TransactionContext.SetResult(commit: false, ack: false);
        transactionScope.Dispose();

        handler.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_when_transaction_is_disposed_without_a_result_does_not_save()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, UnitOfWorkUsingHandler>());
        var activator = new BlueprintApiActivator(provider);
        var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<UnitOfWorkUsingHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage());

        transactionScope.Dispose();

        handler.UnitOfWork.SaveCount.Should().Be(0, "an aborted transaction runs no commit callbacks");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_when_save_fails_then_completing_the_transaction_fails()
    {
        using var provider = BuildProvider(static services =>
        {
            services.AddScoped<IHandleMessages<PlainMessage>, UnitOfWorkUsingHandler>();
            services.AddScoped(static _ => new RecordingUnitOfWork { SaveFailure = new InvalidOperationException("The save failed.") });
        });
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<UnitOfWorkUsingHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage());

        var complete = () => transactionScope.CompleteAsync();

        await complete.Should().ThrowAsync<InvalidOperationException>().WithMessage("The save failed.");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_without_a_registered_unit_of_work_commits_normally()
    {
        using var provider = BuildProvider(
            static services => services.AddScoped<IHandleMessages<PlainMessage>, PlainHandler>(),
            registerUnitOfWork: false);
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<PlainHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage());

        var complete = () => transactionScope.CompleteAsync();

        await complete.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetHandlers_for_operation_message_does_not_save_on_commit()
    {
        // An operation's generated pipeline already saves its own unit of work; saving again on commit would be a
        // second save of the same session.
        RecordingUnitOfWork? unitOfWork = null;

        using var provider = BuildProvider(services =>
        {
            services.AddScoped(_ => unitOfWork = new RecordingUnitOfWork());
            services.AddSingleton(static _ =>
            {
                var app = new BlueprintApp();
                app.Register(new ApiOperationDescriptor(typeof(OperationMessage), nameof(OperationMessage), "test"));

                return app;
            });
        });
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        await activator.GetHandlers(new OperationMessage(), transactionScope.TransactionContext);
        await transactionScope.CompleteAsync();

        unitOfWork.Should().BeNull("the activator must not resolve, let alone save, a unit of work for an operation message");
    }

    [Fact]
    public async Task Rebus_bus_saves_a_plain_handlers_unit_of_work_on_success_and_not_when_the_handler_fails()
    {
        var saves = new SaveCounter();
        var network = new InMemNetwork();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(saves);
            services.AddSingleton(new BlueprintRebusConfiguration());
            services.AddScoped(static sp => new RecordingUnitOfWork { Counter = sp.GetRequiredService<SaveCounter>() });
            services.AddScoped<IHandleMessages<BusMessage>, BusMessageHandler>();
        });

        using var bus = Configure.With(new BlueprintApiActivator(provider))
            .Transport(t => t.UseInMemoryTransport(network, "input"))
            .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 1))
            .Start();

        await bus.SendLocal(new BusMessage { Fail = true });
        await WaitUntilAsync(() => network.GetCount("error") == 1, "the failed message should be moved to the error queue");

        saves.Count.Should().Be(0, "a delivery whose handler threw must not save");

        await bus.SendLocal(new BusMessage { Fail = false });
        await WaitUntilAsync(() => saves.Count == 1, "the successful message's unit of work should be saved");

        network.GetCount("error").Should().Be(1);
    }

    [Fact]
    public async Task Rebus_bus_counts_a_plain_handlers_failing_save_as_a_failed_delivery_attempt_and_dead_letters_the_message()
    {
        // Legacy saved inside the dispatch: its one Rebus handler ran the message's whole pipeline, save included, and
        // rethrew what the pipeline caught (legacy BlueprintApiActivator.cs:80-107; the save is inside the try of
        // FieldDefinitionDeletedEventExecutorPipeline.cs:77-78, whose catch at :101-115 is what the activator rethrows).
        // So a save that failed failed the delivery, which Rebus's retry step counted and, past its limit, moved to the
        // error queue. The save here runs from the received transaction's commit callback instead, and under Rebus 8.6 a
        // commit callback that throws is counted as a failed delivery attempt in the same way, not redelivered without
        // end: this pins that behaviour.
        const int maxDeliveryAttempts = 3;
        var handled = new SaveCounter();
        var network = new InMemNetwork();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(handled);
            services.AddSingleton(new BlueprintRebusConfiguration());
            services.AddScoped(static _ => new RecordingUnitOfWork { SaveFailure = new InvalidOperationException("The save failed.") });
            services.AddScoped<IHandleMessages<BusMessage>, CountingBusMessageHandler>();
        });

        using (var bus = Configure.With(new BlueprintApiActivator(provider))
                   .Transport(t => t.UseInMemoryTransport(network, "input"))
                   .Options(static o => o.RetryStrategy(maxDeliveryAttempts: maxDeliveryAttempts))
                   .Start())
        {
            await bus.SendLocal(new BusMessage());
            await WaitUntilAsync(() => network.GetCount("error") == 1, "a message whose save always fails should be moved to the error queue");
        }

        handled.Count.Should().Be(maxDeliveryAttempts, "each failing save uses up one delivery attempt, and no more are made");

        var deadLettered = network.GetNextOrNull("error");
        deadLettered.Should().NotBeNull();
        deadLettered!.Headers.Should().ContainKey(Headers.ErrorDetails)
            .WhoseValue.Should().Contain("The save failed.", "the save's failure is what the message was given up on for");
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure, bool registerUnitOfWork = true)
    {
        var services = new ServiceCollection();

        // An empty app, so every message type takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddScoped<CallLog>();

        if (registerUnitOfWork)
        {
            services.AddScoped<RecordingUnitOfWork>();
        }

        configure(services);

        if (registerUnitOfWork)
        {
            services.AddScoped<IUnitOfWork>(static sp => sp.GetRequiredService<RecordingUnitOfWork>());
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<THandler> ResolveSingleHandlerAsync<THandler>(
        BlueprintApiActivator activator,
        ITransactionContext transactionContext)
        where THandler : IHandleMessages<PlainMessage>
    {
        var handlers = await activator.GetHandlers(new PlainMessage(), transactionContext);

        return handlers.Should().ContainSingle().Which.Should().BeOfType<THandler>().Subject;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, because);

            await Task.Delay(20);
        }
    }

    private sealed class PlainMessage;

    private sealed class OperationMessage;

    /// <summary>A message the real bus serialises, so it must be public with a settable property.</summary>
    public sealed class BusMessage
    {
        public bool Fail { get; set; }
    }

    private sealed class SaveCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref this._count);

        public void Increment()
        {
            Interlocked.Increment(ref this._count);
        }
    }

    private sealed class CallLog
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Exception? SaveFailure { get; init; }

        public SaveCounter? Counter { get; init; }

        public CallLog? Log { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (this.SaveFailure is not null)
            {
                throw this.SaveFailure;
            }

            this.SaveCount++;
            this.Counter?.Increment();
            this.Log?.Entries.Add("save");

            return Task.CompletedTask;
        }
    }

    private sealed class PlainHandler : IHandleMessages<PlainMessage>
    {
        public Task Handle(PlainMessage message)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkUsingHandler(RecordingUnitOfWork unitOfWork) : IHandleMessages<PlainMessage>
    {
        public RecordingUnitOfWork UnitOfWork => unitOfWork;

        public Task Handle(PlainMessage message)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler(RecordingUnitOfWork unitOfWork) : IHandleMessages<PlainMessage>
    {
        public RecordingUnitOfWork UnitOfWork => unitOfWork;

        public Task Handle(PlainMessage message)
        {
            throw new InvalidOperationException("The handler failed.");
        }
    }

    private sealed class CommitCallbackRegisteringHandler(RecordingUnitOfWork unitOfWork, CallLog log) : IHandleMessages<PlainMessage>
    {
        public CallLog Log => log;

        public Task Handle(PlainMessage message)
        {
            unitOfWork.Log = log;

            AmbientTransactionContext.Current.OnCommit(_ =>
            {
                log.Entries.Add("send");

                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        }
    }

    private sealed class BusMessageHandler(RecordingUnitOfWork unitOfWork) : IHandleMessages<BusMessage>
    {
        public Task Handle(BusMessage message)
        {
            _ = unitOfWork;

            return message.Fail
                ? throw new InvalidOperationException("The handler failed.")
                : Task.CompletedTask;
        }
    }

    private sealed class CountingBusMessageHandler(SaveCounter handled) : IHandleMessages<BusMessage>
    {
        public Task Handle(BusMessage message)
        {
            handled.Increment();

            return Task.CompletedTask;
        }
    }
}
