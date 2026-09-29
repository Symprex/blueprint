using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Runtime.Tasks;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Retry.Simple;
using Rebus.Transport;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="BlueprintApiActivator"/> releases the background tasks a plain (non-operation)
/// <see cref="IHandleMessages{TMessage}"/> enqueued through its scope's <see cref="IBackgroundTaskScheduler"/> when, and
/// only when, Rebus commits the message's transaction, straight after the scope's unit of work is saved.
/// </summary>
/// <remarks>
/// Legacy ran every subscribed message as an operation whose pipeline called <c>IBackgroundTaskScheduler.RunNowAsync()</c>
/// after it completed its Rebus scope and saved the Marten session, inside the same <c>try</c>, so a handler or a save
/// that threw released nothing (legacy <c>AccessTokenRenewalFailedEventExecutorPipeline.cs:74-82</c>, catch <c>:101-115</c>,
/// rethrown by <c>Symprex.Signature365.BlueprintRebus/BlueprintApiActivator.cs:104-107</c>, Signature365
/// <c>8b40ecc961</c>). Its <c>BackgroundTaskRunnerMiddleware</c> matched every operation, Marten or not
/// (<c>Blueprint.Tasks/BackgroundTaskRunnerMiddleware.cs:21-24</c>, registered by <c>TasksConfigurationExtensions.cs:50</c>).
/// </remarks>
public class BlueprintApiActivatorBackgroundTaskTests
{
    [Fact]
    public async Task GetHandlers_for_plain_handler_dispatches_its_enqueued_task_after_the_unit_of_work_save_when_the_transaction_commits()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, EnqueuingHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<EnqueuingHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage { Name = "welcome" });

        handler.Log.Entries.Should().Equal(["handler"], "nothing may be released before Rebus decides the message succeeded");

        await transactionScope.CompleteAsync();

        handler.Log.Entries.Should().Equal("handler", "save", "enqueue welcome");
        handler.Provider.Dispatched.Should().ContainSingle()
            .Which.Task.Should().BeSameAs(handler.EnqueuedTask, "the task released is the one the handler enqueued in its own scope");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_without_a_registered_unit_of_work_still_dispatches_its_enqueued_task()
    {
        using var provider = BuildProvider(
            static services => services.AddScoped<IHandleMessages<PlainMessage>, EnqueuingHandler>(),
            registerUnitOfWork: false);
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<EnqueuingHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage { Name = "welcome" });
        await transactionScope.CompleteAsync();

        handler.Log.Entries.Should().Equal("handler", "enqueue welcome");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_when_handler_throws_does_not_dispatch_its_enqueued_task()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, EnqueuingThenThrowingHandler>());
        var activator = new BlueprintApiActivator(provider);
        var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<EnqueuingThenThrowingHandler>(activator, transactionScope.TransactionContext);

        var handle = () => handler.Handle(new PlainMessage { Name = "welcome" });
        await handle.Should().ThrowExactlyAsync<InvalidOperationException>();

        // As Rebus's retry step records for a failed delivery: no commit, and nack so it is redelivered.
        transactionScope.TransactionContext.SetResult(commit: false, ack: false);
        transactionScope.Dispose();

        handler.Log.Entries.Should().Equal("handler");
        handler.Provider.Dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_when_the_save_fails_does_not_dispatch_its_enqueued_task()
    {
        using var provider = BuildProvider(static services =>
        {
            services.AddScoped<IHandleMessages<PlainMessage>, EnqueuingHandler>();
            services.AddScoped(static sp => new LoggingUnitOfWork(sp.GetRequiredService<CallLog>()) { SaveFailure = new InvalidOperationException("The save failed.") });
        });
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<EnqueuingHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage { Name = "welcome" });

        var complete = () => transactionScope.CompleteAsync();

        await complete.Should().ThrowAsync<InvalidOperationException>().WithMessage("The save failed.");
        handler.Log.Entries.Should().Equal("handler");
        handler.Provider.Dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_dispatches_its_enqueued_task_before_the_enlistments_commit_callback()
    {
        // As a generated pipeline orders them: the save, then the flush, then the Rebus transaction's commit, whose callbacks
        // include the enlistment's and the transport's send (RebusTransactionSaveBoundaryIntegrationTests).
        using var provider = BuildProvider(static services =>
        {
            services.AddScoped<IHandleMessages<PlainMessage>, EnqueuingHandler>();
            services.AddScoped<IRebusTransactionEnlistment, CommitLoggingEnlistment>();
        });
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<EnqueuingHandler>(activator, transactionScope.TransactionContext);
        await handler.Handle(new PlainMessage { Name = "welcome" });
        await transactionScope.CompleteAsync();

        handler.Log.Entries.Should().Equal("enlist", "handler", "save", "enqueue welcome", "enlistment commit");
    }

    [Fact]
    public async Task GetHandlers_for_operation_message_does_not_resolve_a_background_task_scheduler()
    {
        // An operation's generated pipeline already flushes its own scheduler; flushing again on commit would be a second
        // flush of another scope's scheduler.
        IBackgroundTaskScheduler? scheduler = null;

        using var provider = BuildProvider(services =>
        {
            services.AddScoped<IBackgroundTaskScheduler>(sp => scheduler = ActivatorUtilities.CreateInstance<BackgroundTaskScheduler>(sp));
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

        scheduler.Should().BeNull("the activator must not resolve, let alone flush, a scheduler for an operation message");
    }

    [Fact]
    public async Task Rebus_bus_dispatches_a_plain_handlers_enqueued_task_on_success_and_not_when_the_handler_fails()
    {
        var dispatched = new DispatchCounter();
        var network = new InMemNetwork();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(dispatched);
            services.AddSingleton(new BlueprintRebusConfiguration());
            services.AddScoped<IHandleMessages<BusMessage>, EnqueuingBusMessageHandler>();
        });

        using var bus = Configure.With(new BlueprintApiActivator(provider))
            .Transport(t => t.UseInMemoryTransport(network, "input"))
            .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 1))
            .Start();

        await bus.SendLocal(new BusMessage { Fail = true });
        await WaitUntilAsync(() => network.GetCount("error") == 1, "the failed message should be moved to the error queue");

        dispatched.Names.Should().BeEmpty("a delivery whose handler threw must not release what it enqueued");

        await bus.SendLocal(new BusMessage { Fail = false });
        await WaitUntilAsync(() => !dispatched.Names.IsEmpty, "the successful message's task should be dispatched");

        dispatched.Names.Should().Equal("succeeded");
        network.GetCount("error").Should().Be(1);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure, bool registerUnitOfWork = true)
    {
        var services = new ServiceCollection();

        // An empty app, so every message type takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddLogging();
        services.AddScoped<CallLog>();

        // The production scheduler, so what is proven is that the task a handler enqueued reached the provider.
        services.AddScoped<RecordingScheduleProvider>();
        services.AddScoped<IBackgroundTaskScheduleProvider>(static sp => sp.GetRequiredService<RecordingScheduleProvider>());
        services.AddScoped<IBackgroundTaskScheduler, BackgroundTaskScheduler>();

        if (registerUnitOfWork)
        {
            services.AddScoped<LoggingUnitOfWork>();
        }

        configure(services);

        if (registerUnitOfWork)
        {
            services.AddScoped<IUnitOfWork>(static sp => sp.GetRequiredService<LoggingUnitOfWork>());
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

    private sealed class PlainMessage
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class OperationMessage;

    /// <summary>A message the real bus serialises, so it must be public with a settable property.</summary>
    public sealed class BusMessage
    {
        public bool Fail { get; set; }
    }

    private sealed class NamedTask : IBackgroundTask
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class CallLog
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class DispatchCounter
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Names { get; } = new();
    }

    private sealed class RecordingScheduleProvider(CallLog log, IServiceProvider services) : IBackgroundTaskScheduleProvider
    {
        public List<BackgroundTaskEnvelope> Dispatched { get; } = [];

        public Task<string> EnqueueAsync(BackgroundTaskEnvelope task)
        {
            var name = ((NamedTask)task.Task).Name;

            this.Dispatched.Add(task);
            log.Entries.Add($"enqueue {name}");
            services.GetService<DispatchCounter>()?.Names.Enqueue(name);

            return Task.FromResult(name);
        }

        public Task<string> ScheduleAsync(BackgroundTaskEnvelope task, TimeSpan delay)
        {
            throw new NotSupportedException();
        }

        public Task<string> EnqueueChildAsync(BackgroundTaskEnvelope task, string parentId, BackgroundTaskContinuationOptions continuationOptions)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class LoggingUnitOfWork(CallLog log) : IUnitOfWork
    {
        public Exception? SaveFailure { get; init; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (this.SaveFailure is not null)
            {
                throw this.SaveFailure;
            }

            log.Entries.Add("save");

            return Task.CompletedTask;
        }
    }

    private sealed class CommitLoggingEnlistment(CallLog log) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            log.Entries.Add("enlist");

            // As a consumer's outbox check would: registered at enlistment, before any handler runs.
            transactionContext.OnCommit(_ =>
            {
                log.Entries.Add("enlistment commit");

                return Task.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class EnqueuingHandler(IBackgroundTaskScheduler scheduler, RecordingScheduleProvider provider, CallLog log) : IHandleMessages<PlainMessage>
    {
        public CallLog Log => log;

        public RecordingScheduleProvider Provider => provider;

        public NamedTask? EnqueuedTask { get; private set; }

        public Task Handle(PlainMessage message)
        {
            log.Entries.Add("handler");

            this.EnqueuedTask = new NamedTask { Name = message.Name };
            scheduler.Enqueue(this.EnqueuedTask);

            return Task.CompletedTask;
        }
    }

    private sealed class EnqueuingThenThrowingHandler(IBackgroundTaskScheduler scheduler, RecordingScheduleProvider provider, CallLog log) : IHandleMessages<PlainMessage>
    {
        public CallLog Log => log;

        public RecordingScheduleProvider Provider => provider;

        public Task Handle(PlainMessage message)
        {
            log.Entries.Add("handler");

            scheduler.Enqueue(new NamedTask { Name = message.Name });

            throw new InvalidOperationException("The handler failed.");
        }
    }

    private sealed class EnqueuingBusMessageHandler(IBackgroundTaskScheduler scheduler) : IHandleMessages<BusMessage>
    {
        public Task Handle(BusMessage message)
        {
            scheduler.Enqueue(new NamedTask { Name = message.Fail ? "failed" : "succeeded" });

            return message.Fail
                ? throw new InvalidOperationException("The handler failed.")
                : Task.CompletedTask;
        }
    }
}
