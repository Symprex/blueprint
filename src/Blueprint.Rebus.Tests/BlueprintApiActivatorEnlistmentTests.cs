using Blueprint.Model;
using Blueprint.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Handlers;
using Rebus.Transport;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="BlueprintApiActivator"/> calls the consumer's <see cref="IRebusTransactionEnlistment"/>s with a
/// received message's transaction when a plain handler's unit of work will be saved in it, and in the order an outbox
/// needs: enlisted before any handler runs, and the unit of work saved before any commit callback the enlistment adds.
/// </summary>
public class BlueprintApiActivatorEnlistmentTests
{
    [Fact]
    public async Task GetHandlers_for_plain_handler_enlists_before_the_handler_and_saves_before_the_enlistments_commit_callback()
    {
        using var provider = BuildProvider(static services => services.AddScoped<IHandleMessages<PlainMessage>, LoggingHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handlers = await activator.GetHandlers(new PlainMessage(), transactionScope.TransactionContext);
        var handler = handlers.Should().ContainSingle().Which.Should().BeOfType<LoggingHandler>().Subject;
        await handler.Handle(new PlainMessage());
        await transactionScope.CompleteAsync();

        handler.Log.Entries.Should().Equal("enlist", "handler", "save", "enlistment commit");

        var enlistment = handler.Enlistment;
        enlistment.TransactionContext.Should().BeSameAs(transactionScope.TransactionContext, "the received message's transaction is the one enlisted");
        enlistment.Services!.GetRequiredService<CallLog>().Should().BeSameAs(handler.Log, "the enlistment is given the handlers' own scope");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_without_a_unit_of_work_does_not_enlist()
    {
        // Nothing will be saved in the transaction, so an outbox enlisted in it would never be written.
        using var provider = BuildProvider(
            static services => services.AddScoped<IHandleMessages<PlainMessage>, LoggingHandler>(),
            registerUnitOfWork: false);
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = (LoggingHandler)(await activator.GetHandlers(new PlainMessage(), transactionScope.TransactionContext)).Single();
        await handler.Handle(new PlainMessage());
        await transactionScope.CompleteAsync();

        handler.Log.Entries.Should().Equal("handler");
    }

    [Fact]
    public async Task GetHandlers_for_operation_message_does_not_enlist()
    {
        // An operation's generated pipeline opens, enlists and completes its own scope.
        CallLog? log = null;

        using var provider = BuildProvider(services =>
        {
            services.AddScoped(_ => log = new CallLog());
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

        log.Should().BeNull("the activator must not resolve an enlistment for an operation message");
    }

    [Fact]
    public async Task GetHandlers_when_an_enlistment_throws_fails_and_still_disposes_the_scope_with_the_transaction()
    {
        using var provider = BuildProvider(static services =>
        {
            services.AddScoped<IHandleMessages<PlainMessage>, LoggingHandler>();
            services.AddScoped<IRebusTransactionEnlistment, ThrowingEnlistment>();
        });
        var activator = new BlueprintApiActivator(provider);
        var transactionScope = new RebusTransactionScope();

        var getHandlers = () => activator.GetHandlers(new PlainMessage(), transactionScope.TransactionContext);

        await getHandlers.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("The enlistment failed.");

        transactionScope.Dispose();
        ThrowingEnlistment.LastResource!.DisposeCount.Should().Be(1, "a failed enlistment must not leak the scope");
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure, bool registerUnitOfWork = true)
    {
        var services = new ServiceCollection();

        // An empty app, so every message type takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddScoped<CallLog>();
        services.AddScoped<DisposableResource>();
        services.AddScoped<RecordingEnlistment>();
        services.AddScoped<IRebusTransactionEnlistment>(static sp => sp.GetRequiredService<RecordingEnlistment>());

        if (registerUnitOfWork)
        {
            services.AddScoped<IUnitOfWork, LoggingUnitOfWork>();
        }

        configure(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class PlainMessage;

    private sealed class OperationMessage;

    private sealed class CallLog
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class DisposableResource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            this.DisposeCount++;
        }
    }

    private sealed class RecordingEnlistment(CallLog log) : IRebusTransactionEnlistment
    {
        public ITransactionContext? TransactionContext { get; private set; }

        public IServiceProvider? Services { get; private set; }

        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            this.TransactionContext = transactionContext;
            this.Services = services;
            log.Entries.Add("enlist");

            // As a consumer's outbox check would: registered at enlistment, so it must still run after the save.
            transactionContext.OnCommit(_ =>
            {
                log.Entries.Add("enlistment commit");

                return Task.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingEnlistment(DisposableResource resource) : IRebusTransactionEnlistment
    {
        public static DisposableResource? LastResource { get; private set; }

        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            LastResource = resource;

            throw new InvalidOperationException("The enlistment failed.");
        }
    }

    private sealed class LoggingUnitOfWork(CallLog log) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            log.Entries.Add("save");

            return Task.CompletedTask;
        }
    }

    private sealed class LoggingHandler(CallLog log, RecordingEnlistment enlistment) : IHandleMessages<PlainMessage>
    {
        public CallLog Log => log;

        public RecordingEnlistment Enlistment => enlistment;

        public Task Handle(PlainMessage message)
        {
            log.Entries.Add("handler");

            return Task.CompletedTask;
        }
    }
}
