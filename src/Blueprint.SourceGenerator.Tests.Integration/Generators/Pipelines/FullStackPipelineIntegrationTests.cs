using System.Diagnostics;
using Blueprint.Auditing;
using Blueprint.Messaging;
using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that ALL pipeline modules cooperate correctly in a single command execution,
/// verifying the complete Signature365-equivalent pipeline: validation → handler → audit →
/// unit-of-work save → message outbox commit → background task flush.
/// This is the definitive proof that the source-generated pipeline composition produces
/// the correct execution order and short-circuit behavior.
/// </summary>
public class FullStackPipelineIntegrationTests
{
    [Fact]
    public async Task When_command_succeeds_then_all_post_execution_modules_fire_in_correct_order()
    {
        var tracker = new ExecutionOrderTracker();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var serviceProvider = new FullStackTestServiceProvider(tracker);

        var pipeline = executor.GetPipeline(typeof(FullStackCommand));
        var context = new ApiOperationContext(serviceProvider, app, FullStackCommand.Configure(), CancellationToken.None);
        ((FullStackCommand)context.Operation).Name = "full-stack-valid";

        using var activity = new Activity("full-stack-test");
        activity.Start();
        context.Activity = activity;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeOfType<ValidationFailedResult>("valid input should not trigger validation failure");

        // Verify audit was called (only after successful handler)
        tracker.AuditSink.RecordCallCount.Should().Be(1, "audit sink should fire after successful handler execution");

        // Verify UoW saved changes
        tracker.UnitOfWork.SaveChangesCallCount.Should().Be(1, "unit of work should save after successful execution");

        // Verify message outbox committed
        tracker.MessageOutbox.CommitCallCount.Should().Be(1, "message outbox should commit after handler success");

        // Verify background task scheduler flushed
        tracker.BackgroundTaskScheduler.FlushCallCount.Should().Be(1, "background tasks should flush after post-execution");

        // Verify audit activity events were emitted
        var events = activity.Events.ToList();
        events.Should().Contain(
            static e => e.Name == "blueprint.audit.started",
            "audit started event should be emitted");
        events.Should().Contain(
            static e => e.Name == "blueprint.audit.succeeded",
            "audit succeeded event should be emitted after successful sink call");
    }

    [Fact]
    public async Task When_command_fails_validation_then_no_post_execution_modules_fire()
    {
        var tracker = new ExecutionOrderTracker();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var serviceProvider = new FullStackTestServiceProvider(tracker);

        var pipeline = executor.GetPipeline(typeof(FullStackCommand));
        var context = new ApiOperationContext(serviceProvider, app, FullStackCommand.Configure(), CancellationToken.None);

        // Leave Name null to trigger validation failure
        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>("null [Required] field should produce validation failure");

        // NONE of the post-execution modules should fire
        tracker.AuditSink.RecordCallCount.Should().Be(0, "audit should not fire on validation failure");
        tracker.UnitOfWork.SaveChangesCallCount.Should().Be(0, "UoW should not save on validation failure");
        tracker.MessageOutbox.CommitCallCount.Should().Be(0, "outbox should not commit on validation failure");
        tracker.BackgroundTaskScheduler.FlushCallCount.Should().Be(0, "tasks should not flush on validation failure");
    }

    [Fact]
    public async Task When_audit_sink_throws_then_pipeline_continues_and_uow_and_outbox_still_succeed()
    {
        var tracker = new ExecutionOrderTracker(throwOnAudit: true);
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var serviceProvider = new FullStackTestServiceProvider(tracker);

        var pipeline = executor.GetPipeline(typeof(FullStackCommand));
        var context = new ApiOperationContext(serviceProvider, app, FullStackCommand.Configure(), CancellationToken.None);
        ((FullStackCommand)context.Operation).Name = "audit-will-throw";

        using var activity = new Activity("audit-throws-test");
        activity.Start();
        context.Activity = activity;

        var result = await pipeline.ExecuteAsync(context);

        // Pipeline should still return handler result (audit is best-effort)
        result.Should().NotBeOfType<ValidationFailedResult>();

        // UoW and outbox should still have succeeded (they run before audit in pipeline order:
        // UoW=900 < Audit=950 < BackgroundTaskFlush=1000)
        tracker.UnitOfWork.SaveChangesCallCount.Should().Be(1, "UoW should save regardless of audit failure");
        tracker.MessageOutbox.CommitCallCount.Should().Be(1, "outbox should commit regardless of audit failure");
        tracker.BackgroundTaskScheduler.FlushCallCount.Should().Be(1, "tasks should flush regardless of audit failure");

        // Audit failure event should be recorded on activity
        var events = activity.Events.ToList();
        events.Should().Contain(
            static e => e.Name == "blueprint.audit.failed",
            "audit failed event should be emitted when sink throws");
    }

    [Fact]
    public async Task When_command_succeeds_then_execution_order_is_uow_then_outbox_then_audit_then_tasks()
    {
        var tracker = new ExecutionOrderTracker();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var serviceProvider = new FullStackTestServiceProvider(tracker);

        var pipeline = executor.GetPipeline(typeof(FullStackCommand));
        var context = new ApiOperationContext(serviceProvider, app, FullStackCommand.Configure(), CancellationToken.None);
        ((FullStackCommand)context.Operation).Name = "order-test";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeOfType<ValidationFailedResult>();

        // The pipeline execution order (by module Order) should be:
        // UoW (900) → Audit (950) → BackgroundTaskFlush (1000)
        // MessageOutbox (910) is between UoW and Audit
        tracker.ExecutionLog.Should().ContainInOrder(
            "UnitOfWork.SaveChanges",
            "MessageOutbox.Commit",
            "AuditSink.Record",
            "BackgroundTaskScheduler.Flush");
    }

    [Fact]
    public async Task When_handler_throws_then_no_post_execution_modules_fire()
    {
        var tracker = new ExecutionOrderTracker();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var serviceProvider = new FullStackTestServiceProvider(tracker);

        var pipeline = executor.GetPipeline(typeof(ThrowingFullStackCommand));
        var context = new ApiOperationContext(serviceProvider, app, ThrowingFullStackCommand.Configure(), CancellationToken.None);
        ((ThrowingFullStackCommand)context.Operation).Name = "will-throw";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>(
            "handler exception should be caught and translated to UnhandledExceptionResult");

        // NONE of the post-execution modules should fire when handler throws
        tracker.UnitOfWork.SaveChangesCallCount.Should().Be(0,
            "UoW should not save when handler throws — data consistency requires rollback");
        tracker.MessageOutbox.CommitCallCount.Should().Be(0,
            "outbox should not commit when handler throws — messages must not be published for failed operations");
        tracker.AuditSink.RecordCallCount.Should().Be(0,
            "audit should not record when handler throws (exception is logged separately)");
        tracker.BackgroundTaskScheduler.FlushCallCount.Should().Be(0,
            "background tasks should not flush when handler throws");
        tracker.ExecutionLog.Should().BeEmpty(
            "no post-execution steps should appear in execution log");
    }

    // ─── Service provider ────────────────────────────────────────────────────────

    private sealed class FullStackTestServiceProvider(ExecutionOrderTracker tracker) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IUnitOfWork))
            {
                return tracker.UnitOfWork;
            }

            if (serviceType == typeof(IMessageOutbox))
            {
                return tracker.MessageOutbox;
            }

            if (serviceType == typeof(IAuditSink))
            {
                return tracker.AuditSink;
            }

            if (serviceType == typeof(IBackgroundTaskScheduler))
            {
                return tracker.BackgroundTaskScheduler;
            }

            return null;
        }
    }

    // ─── Tracking infrastructure ─────────────────────────────────────────────────

    private sealed class ExecutionOrderTracker
    {
        private readonly bool _throwOnAudit;

        public ExecutionOrderTracker(bool throwOnAudit = false)
        {
            this._throwOnAudit = throwOnAudit;
            this.UnitOfWork = new OrderTrackingUnitOfWork(this);
            this.MessageOutbox = new OrderTrackingMessageOutbox(this);
            this.AuditSink = new OrderTrackingAuditSink(this, throwOnAudit);
            this.BackgroundTaskScheduler = new OrderTrackingBackgroundTaskScheduler(this);
        }

        public List<string> ExecutionLog { get; } = [];

        public OrderTrackingUnitOfWork UnitOfWork { get; }

        public OrderTrackingMessageOutbox MessageOutbox { get; }

        public OrderTrackingAuditSink AuditSink { get; }

        public OrderTrackingBackgroundTaskScheduler BackgroundTaskScheduler { get; }
    }

    private sealed class OrderTrackingUnitOfWork(ExecutionOrderTracker tracker) : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            this.SaveChangesCallCount++;
            tracker.ExecutionLog.Add("UnitOfWork.SaveChanges");
            return Task.CompletedTask;
        }
    }

    private sealed class OrderTrackingMessageOutbox(ExecutionOrderTracker tracker) : IMessageOutbox
    {
        public int CommitCallCount { get; private set; }

        public void Publish(object message)
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            this.CommitCallCount++;
            tracker.ExecutionLog.Add("MessageOutbox.Commit");
            return Task.CompletedTask;
        }
    }

    private sealed class OrderTrackingAuditSink(ExecutionOrderTracker tracker, bool shouldThrow) : IAuditSink
    {
        public int RecordCallCount { get; private set; }

        public Task RecordAsync(ApiOperationContext context, OperationResult result, CancellationToken cancellationToken)
        {
            this.RecordCallCount++;
            tracker.ExecutionLog.Add("AuditSink.Record");

            if (shouldThrow)
            {
                throw new InvalidOperationException("Simulated audit sink failure for full-stack test");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class OrderTrackingBackgroundTaskScheduler(ExecutionOrderTracker tracker) : IBackgroundTaskScheduler
    {
        public int FlushCallCount { get; private set; }

        public IScheduledBackgroundTask Enqueue(IBackgroundTask task)
        {
            return new NoOpScheduledTask();
        }

        public IScheduledBackgroundTask Schedule(IBackgroundTask task, TimeSpan delay)
        {
            return new NoOpScheduledTask();
        }

        public ValueTask RunNowAsync()
        {
            this.FlushCallCount++;
            tracker.ExecutionLog.Add("BackgroundTaskScheduler.Flush");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpScheduledTask : IScheduledBackgroundTask
    {
        public IScheduledBackgroundTask ContinueWith(IBackgroundTask task, BackgroundTaskContinuationOptions options)
        {
            return this;
        }
    }
}

/// <summary>
/// A command that participates in ALL pipeline stages: validation (Required field),
/// handler execution, audit recording, unit-of-work save, message outbox commit, and
/// background task flush. Used by <see cref="FullStackPipelineIntegrationTests"/> to
/// prove the complete pipeline composition.
/// </summary>
public partial class FullStackCommand : ICommand
{
    /// <summary>Gets or sets the required name (triggers validation when null).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    /// <summary>Handles the command by returning a successful result.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(FullStackCommand operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// A command whose handler throws an exception, used to prove that ALL post-execution
/// modules (UoW, outbox, audit, background tasks) are skipped when the handler fails.
/// </summary>
public partial class ThrowingFullStackCommand : ICommand
{
    /// <summary>Gets or sets the required name (triggers validation when null).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    /// <summary>Handles the command by throwing — proves no post-execution modules fire.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task HandleAsync(ThrowingFullStackCommand operation)
    {
        throw new InvalidOperationException("Simulated handler failure for full-stack exception test");
    }
}
