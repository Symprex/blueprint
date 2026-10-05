using System.Text.Json;
using Blueprint.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

// Hangfire uses expression trees for job dispatch — the async call in the lambda is never
// awaited by our code; Hangfire serializes the expression and invokes it in its own pipeline.
#pragma warning disable CS4014 // Because this call is not awaited

namespace Blueprint.Hangfire;

/// <summary>
/// Implements <see cref="IBackgroundTaskScheduleProvider"/> using Hangfire's
/// <see cref="IBackgroundJobClient"/> for durable background task execution.
/// Tasks are serialized as JSON and dispatched to Hangfire, which handles
/// persistence, retry, and queue routing.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="BackgroundTaskEnvelope"/> is serialized to JSON and passed to
/// <see cref="HangfireBackgroundTaskExecutor.ExecuteAsync"/> for deserialization and
/// dispatch through the Blueprint pipeline.
/// </para>
/// <para>
/// Queue routing uses the <see cref="Blueprint.Tasks.QueueAttribute"/> on the task type when present,
/// falling back to the app's <see cref="BlueprintTaskOptions.DefaultQueue"/>, for enqueued, scheduled and
/// continuation jobs alike.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HangfireBackgroundTaskScheduleProvider : IBackgroundTaskScheduleProvider
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly string _defaultQueue;

    /// <summary>
    /// Initialises the provider with the Hangfire job client, routing tasks without a <see cref="Blueprint.Tasks.QueueAttribute"/> to
    /// Hangfire's <c>default</c> queue.
    /// </summary>
    /// <param name="jobClient">The Hangfire job client for enqueueing jobs.</param>
    public HangfireBackgroundTaskScheduleProvider(IBackgroundJobClient jobClient)
        : this(jobClient, Options.Create(new BlueprintTaskOptions()))
    {
    }

    /// <summary>
    /// Initialises the provider with the Hangfire job client and the app's task options, whose
    /// <see cref="BlueprintTaskOptions.DefaultQueue"/> receives every task without a <see cref="Blueprint.Tasks.QueueAttribute"/>.
    /// </summary>
    /// <param name="jobClient">The Hangfire job client for enqueueing jobs.</param>
    /// <param name="taskOptions">The app's background task options.</param>
    public HangfireBackgroundTaskScheduleProvider(IBackgroundJobClient jobClient, IOptions<BlueprintTaskOptions> taskOptions)
    {
        ArgumentNullException.ThrowIfNull(jobClient);
        ArgumentNullException.ThrowIfNull(taskOptions);

        this._jobClient = jobClient;
        this._defaultQueue = taskOptions.Value.DefaultQueue ?? EnqueuedState.DefaultQueue;
    }

    /// <inheritdoc />
    public Task<string> EnqueueAsync(BackgroundTaskEnvelope envelope)
    {
        return Task.FromResult(this.CreateJob(envelope, static queue => new EnqueuedState(queue)));
    }

    /// <inheritdoc />
    public Task<string> ScheduleAsync(BackgroundTaskEnvelope envelope, TimeSpan delay)
    {
        return Task.FromResult(this.CreateJob(envelope, _ => new ScheduledState(delay)));
    }

    /// <inheritdoc />
    public Task<string> EnqueueChildAsync(
        BackgroundTaskEnvelope envelope,
        string parentId,
        BackgroundTaskContinuationOptions continuationOptions)
    {
        var hangfireOptions = continuationOptions switch
        {
            BackgroundTaskContinuationOptions.OnlyOnSucceededState => JobContinuationOptions.OnlyOnSucceededState,
            BackgroundTaskContinuationOptions.OnAnyFinishedState => JobContinuationOptions.OnAnyFinishedState,
            _ => JobContinuationOptions.OnlyOnSucceededState,
        };

        return Task.FromResult(
            this.CreateJob(envelope, queue => new AwaitingState(parentId, new EnqueuedState(queue), hangfireOptions)));
    }

    /// <summary>
    /// Creates the job on the task's queue — its <see cref="Blueprint.Tasks.QueueAttribute"/>, else the app's default
    /// queue — in the state <paramref name="createState"/> builds for that queue.
    /// </summary>
    /// <remarks>
    /// The queue is set on the job itself as well as on any enqueued state, as legacy Blueprint set it. A scheduled or
    /// awaiting job is enqueued only later, when it falls due or its parent finishes, and Hangfire then enqueues it to
    /// the job's own queue; without one it would land on Hangfire's <c>default</c> queue, which a server listening only to
    /// the app's queue never processes. The enqueued state carries it too, for storage that cannot record a job's queue.
    /// </remarks>
    private string CreateJob(BackgroundTaskEnvelope envelope, Func<string, IState> createState)
    {
        var payload = SerializeEnvelope(envelope);
        var queue = envelope.GetQueueName() ?? this._defaultQueue;

        var job = Job.FromExpression<HangfireBackgroundTaskExecutor>(
            executor => executor.ExecuteAsync(payload, CancellationToken.None),
            queue);

        return this._jobClient.Create(job, createState(queue));
    }

    private static string SerializeEnvelope(BackgroundTaskEnvelope envelope)
    {
        return JsonSerializer.Serialize(new HangfireTaskPayload
        {
            TaskTypeName = envelope.Task.GetType().AssemblyQualifiedName!,
            TaskJson = JsonSerializer.Serialize(envelope.Task, envelope.Task.GetType()),
            Headers = envelope.Headers is not null
                ? new Dictionary<string, string>(envelope.Headers)
                : null,
        });
    }
}
