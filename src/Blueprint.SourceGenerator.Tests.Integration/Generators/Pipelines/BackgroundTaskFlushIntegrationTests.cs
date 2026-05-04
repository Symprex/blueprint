using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that background tasks enqueued during handler execution are flushed in the PostExecution
/// stage of the generated pipeline, but only when the handler succeeds.
/// </summary>
public class BackgroundTaskFlushIntegrationTests
{
    [Fact]
    public async Task When_handler_enqueues_task_and_succeeds_then_scheduler_flushes_after_execution()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var descriptor = app.GetOperationsForHost("tasks")
            .First(static op => op.OperationType == typeof(TaskThatEnqueuesWork));

        var scheduler = new TestBackgroundTaskScheduler();
        var serviceProvider = new TestServiceProvider(scheduler);

        var pipeline = executor.GetPipeline(typeof(TaskThatEnqueuesWork));
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None);
        ((TaskThatEnqueuesWork)context.Operation).Payload = "flush-me";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        scheduler.EnqueuedTasks.Should().HaveCount(1);
        scheduler.FlushCallCount.Should().Be(1, "flush should be called exactly once after successful handler execution");
    }

    [Fact]
    public async Task When_handler_succeeds_but_no_scheduler_registered_then_pipeline_completes_without_error()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var descriptor = app.GetOperationsForHost("tasks")
            .First(static op => op.OperationType == typeof(DispatchableBackgroundTask));

        var serviceProvider = new TestServiceProvider(scheduler: null);

        var pipeline = executor.GetPipeline(typeof(DispatchableBackgroundTask));
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None);
        ((DispatchableBackgroundTask)context.Operation).Payload = "no-scheduler";

        var result = await pipeline.ExecuteAsync(context);

        // Pipeline should complete — the flush module gracefully skips when no scheduler is registered
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task When_handler_fails_validation_then_scheduler_is_not_flushed()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var descriptor = app.GetOperationsForHost("tasks")
            .First(static op => op.OperationType == typeof(ValidatedTaskThatEnqueuesWork));

        var scheduler = new TestBackgroundTaskScheduler();
        var serviceProvider = new TestServiceProvider(scheduler);

        var pipeline = executor.GetPipeline(typeof(ValidatedTaskThatEnqueuesWork));
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        scheduler.FlushCallCount.Should().Be(0, "flush should NOT be called when validation fails");
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly IBackgroundTaskScheduler? _scheduler;

        public TestServiceProvider(IBackgroundTaskScheduler? scheduler)
        {
            this._scheduler = scheduler;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IBackgroundTaskScheduler))
            {
                return this._scheduler;
            }

            return null;
        }
    }
}

/// <summary>
/// Test scheduler that records enqueued tasks and flush calls for assertion.
/// </summary>
public sealed class TestBackgroundTaskScheduler : IBackgroundTaskScheduler
{
    /// <summary>Gets the tasks that were enqueued during handler execution.</summary>
    public List<object> EnqueuedTasks { get; } = [];

    /// <summary>Gets the number of times FlushAsync was called.</summary>
    public int FlushCallCount { get; private set; }

    /// <inheritdoc />
    public void Enqueue(IBackgroundTask task)
    {
        this.EnqueuedTasks.Add(task);
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        this.FlushCallCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A background task whose handler enqueues follow-on work via the scheduler.
/// </summary>
public partial class TaskThatEnqueuesWork : IBackgroundTask
{
    /// <summary>Gets or sets the payload for this task.</summary>
    public string? Payload { get; set; }

    /// <summary>Handles the task by enqueuing follow-on work.</summary>
    /// <param name="operation">The task operation instance.</param>
    /// <param name="scheduler">The injected background task scheduler.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(TaskThatEnqueuesWork operation, IBackgroundTaskScheduler scheduler)
    {
        scheduler.Enqueue(new FollowOnTask { Source = operation.Payload });
        return Task.CompletedTask;
    }
}

/// <summary>
/// A validated background task whose handler also enqueues follow-on work.
/// Proves that validation failure prevents flush.
/// </summary>
public partial class ValidatedTaskThatEnqueuesWork : IBackgroundTask
{
    /// <summary>Gets or sets the required field.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    /// <summary>Handles the validated task by enqueuing follow-on work.</summary>
    /// <param name="operation">The task operation instance.</param>
    /// <param name="scheduler">The injected background task scheduler.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(ValidatedTaskThatEnqueuesWork operation, IBackgroundTaskScheduler scheduler)
    {
        scheduler.Enqueue(new FollowOnTask { Source = operation.Name });
        return Task.CompletedTask;
    }
}

/// <summary>
/// A trivial follow-on task used by the flush tests.
/// </summary>
public partial class FollowOnTask : IBackgroundTask
{
    /// <summary>Gets or sets the source of this follow-on task.</summary>
    public string? Source { get; set; }

    /// <summary>No-op handler for the follow-on task.</summary>
    /// <param name="operation">The follow-on task instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(FollowOnTask operation)
    {
        return Task.CompletedTask;
    }
}
