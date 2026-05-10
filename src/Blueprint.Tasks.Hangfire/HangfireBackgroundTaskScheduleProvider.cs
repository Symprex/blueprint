using System.Diagnostics;
using Blueprint.Runtime.Tasks;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blueprint.Tasks.Hangfire;

/// <summary>
/// An <see cref="IBackgroundTaskScheduleProvider"/> that dispatches tasks to Hangfire by creating
/// <see cref="global::Hangfire.Common.Job"/> expressions targeting <see cref="HangfireTaskExecutor.ExecuteAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each dispatched task is wrapped in a <see cref="HangfireBackgroundTaskWrapper"/> for dashboard-friendly
/// display. Trace context is injected into the envelope headers so background execution links back
/// to the originating request activity.
/// </para>
/// </remarks>
public sealed class HangfireBackgroundTaskScheduleProvider : IBackgroundTaskScheduleProvider
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly ILogger<HangfireBackgroundTaskScheduleProvider> _logger;
    private readonly string _defaultQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="HangfireBackgroundTaskScheduleProvider"/> class.
    /// </summary>
    /// <param name="jobClient">The Hangfire background job client.</param>
    /// <param name="taskOptions">Blueprint task options containing the default queue name.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public HangfireBackgroundTaskScheduleProvider(
        IBackgroundJobClient jobClient,
        IOptions<BlueprintTaskOptions> taskOptions,
        ILogger<HangfireBackgroundTaskScheduleProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(jobClient);
        ArgumentNullException.ThrowIfNull(taskOptions);

        this._jobClient = jobClient;
        this._logger = logger;
        this._defaultQueue = taskOptions.Value.DefaultQueue;
    }

    /// <inheritdoc />
    public Task<string> EnqueueAsync(BackgroundTaskEnvelope task)
    {
        return Task.FromResult(this.CreateCore(task, static queue => new EnqueuedState(queue)));
    }

    /// <inheritdoc />
    public Task<string> ScheduleAsync(BackgroundTaskEnvelope task, TimeSpan delay)
    {
        return Task.FromResult(this.CreateCore(task, _ => new ScheduledState(delay)));
    }

    /// <inheritdoc />
    public Task<string> EnqueueChildAsync(
        BackgroundTaskEnvelope task,
        string parentId,
        BackgroundTaskContinuationOptions continuationOptions)
    {
        var hangfireOptions = continuationOptions == BackgroundTaskContinuationOptions.OnlyOnSucceededState
            ? JobContinuationOptions.OnlyOnSucceededState
            : JobContinuationOptions.OnAnyFinishedState;

        return Task.FromResult(
            this.CreateCore(
                task,
                queue => new AwaitingState(parentId, new EnqueuedState(queue), hangfireOptions)));
    }

    private string CreateCore(BackgroundTaskEnvelope task, Func<string, IState> createState)
    {
        var queue = task.GetQueueName() ?? this._defaultQueue;

        if (this._logger.IsEnabled(LogLevel.Debug))
        {
            this._logger.LogDebug(
                "Enqueuing task {TaskType} to queue {Queue}",
                task.Task.GetType().Name,
                queue);
        }

        var wrapper = new HangfireBackgroundTaskWrapper(task);

        // Inject trace context into headers so the executor can link back to this span.
        task.Headers ??= new Dictionary<string, string>();

        using var activity = TaskExecutor.ActivitySource.StartActivity(
            $"{task.Task.GetType().Name} send",
            ActivityKind.Producer);

        var contextToInject = activity?.Context ?? Activity.Current?.Context ?? default;

        if (contextToInject != default)
        {
            task.Headers["traceparent"] = contextToInject.TraceId + "-" + contextToInject.SpanId + "-" + ((int)contextToInject.TraceFlags).ToString("00");

            if (!string.IsNullOrEmpty(contextToInject.TraceState))
            {
                task.Headers["tracestate"] = contextToInject.TraceState;
            }
        }

        activity?.SetTag("messaging.system", "hangfire");
        activity?.SetTag("messaging.destination", queue);

        var id = this._jobClient.Create(
            global::Hangfire.Common.Job.FromExpression<HangfireTaskExecutor>(
                e => e.ExecuteAsync(wrapper, null!, CancellationToken.None),
                queue),
            createState(queue));

        return id;
    }
}
