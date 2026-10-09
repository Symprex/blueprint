using Blueprint.Auditing;
using Blueprint.Messaging;
using Blueprint.Persistence;
using Blueprint.Rebus;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Rebus.Bus;
using Rebus.Transport;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Pins where a generated pipeline's unit of work save falls in its Rebus transaction: after the handler, and before the
/// <see cref="IMessageOutbox"/> commit, the audit sink and the background-task flush, all of which run inside the
/// transaction before it is completed. A message any of those three sends reaches the transaction after the save, so an
/// outbox that writes the transaction's messages into the unit of work during the save does not see it.
/// </summary>
/// <remarks>
/// Legacy did not have this window: it completed the Rebus scope before it saved the Marten session (legacy
/// <c>AzureAdOauthReturnCommandExecutorPipeline.cs:133-137</c> at Signature365 <c>8b40ecc961</c>), and ran the background-task
/// flush and the resource-event audit after both, outside any scope (<c>:139-149</c>).
/// </remarks>
public class RebusTransactionSaveBoundaryIntegrationTests
{
    [Fact]
    public async Task When_command_saves_a_unit_of_work_then_the_outbox_commit_audit_and_task_flush_run_after_the_save_inside_the_transaction()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, provider);
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(SaveBoundaryCommand)), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        var log = scope.ServiceProvider.GetRequiredService<SaveBoundaryLog>();

        var result = await executor.GetPipeline(typeof(SaveBoundaryCommand)).ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal(
            "enlist",
            "handler",
            "save",
            "message outbox commit",
            "audit",
            "background task flush",
            "rebus commit");
        log.AmbientDuringPostSaveSteps.Should().OnlyContain(
            ambient => ReferenceEquals(ambient, log.EnlistedTransaction),
            "each post-save step runs inside the transaction the enlistment was given, so what it sends joins that transaction");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // The pipeline opens its Rebus scope only when an IBus is registered.
        services.AddSingleton(Substitute.For<IBus>());
        services.AddScoped<SaveBoundaryLog>();
        services.AddScoped<IRebusTransactionEnlistment, LoggingEnlistment>();
        services.AddScoped<IUnitOfWork, LoggingUnitOfWork>();
        services.AddScoped<IMessageOutbox, LoggingMessageOutbox>();
        services.AddScoped<IBackgroundTaskScheduler, LoggingBackgroundTaskScheduler>();

        // A root singleton, as the pipeline shell resolves it from the root provider; it logs to the operation's scope.
        services.AddSingleton<IAuditSink, LoggingAuditSink>();

        return services.BuildServiceProvider();
    }

    private sealed class LoggingEnlistment(SaveBoundaryLog log) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            log.Entries.Add("enlist");
            log.EnlistedTransaction = transactionContext;

            // Stands in for the transport's send callback, which a message sent anywhere in the pipeline goes out from.
            transactionContext.OnCommit(_ =>
            {
                log.Entries.Add("rebus commit");

                return Task.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoggingUnitOfWork(SaveBoundaryLog log) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            log.Entries.Add("save");

            return Task.CompletedTask;
        }
    }

    private sealed class LoggingMessageOutbox(SaveBoundaryLog log) : IMessageOutbox
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            log.Record("message outbox commit");

            return Task.CompletedTask;
        }
    }

    private sealed class LoggingAuditSink : IAuditSink
    {
        public Task RecordAsync(ApiOperationContext context, OperationResult result, CancellationToken cancellationToken)
        {
            context.ServiceProvider.GetRequiredService<SaveBoundaryLog>().Record("audit");

            return Task.CompletedTask;
        }
    }

    private sealed class LoggingBackgroundTaskScheduler(SaveBoundaryLog log) : IBackgroundTaskScheduler
    {
        public IScheduledBackgroundTask Enqueue(IBackgroundTask task)
        {
            throw new NotSupportedException();
        }

        public IScheduledBackgroundTask Schedule(IBackgroundTask task, TimeSpan delay)
        {
            throw new NotSupportedException();
        }

        public ValueTask RunNowAsync()
        {
            log.Record("background task flush");

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Records, in order, what the steps around a generated pipeline's save did, and the transaction each ran in.</summary>
public sealed class SaveBoundaryLog
{
    public List<string> Entries { get; } = [];

    public ITransactionContext? EnlistedTransaction { get; set; }

    public List<ITransactionContext?> AmbientDuringPostSaveSteps { get; } = [];

    public void Record(string entry)
    {
        this.Entries.Add(entry);
        this.AmbientDuringPostSaveSteps.Add(AmbientTransactionContext.Current);
    }
}

/// <summary>A command whose pipeline saves the unit of work, commits the message outbox, audits and flushes tasks.</summary>
public partial class SaveBoundaryCommand : Blueprint.Model.ICommand
{
    public static Task HandleAsync(SaveBoundaryCommand operation, SaveBoundaryLog log)
    {
        log.Entries.Add("handler");

        return Task.CompletedTask;
    }
}
