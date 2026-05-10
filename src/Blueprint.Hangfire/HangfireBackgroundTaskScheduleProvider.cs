using System.Text.Json;
using Blueprint.Tasks;
using Hangfire;
using JetBrains.Annotations;

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
/// falling back to the <c>"default"</c> queue.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HangfireBackgroundTaskScheduleProvider : IBackgroundTaskScheduleProvider
{
    private readonly IBackgroundJobClient _jobClient;

    /// <summary>
    /// Initialises the provider with the Hangfire job client.
    /// </summary>
    /// <param name="jobClient">The Hangfire job client for enqueueing jobs.</param>
    public HangfireBackgroundTaskScheduleProvider(IBackgroundJobClient jobClient)
    {
        this._jobClient = jobClient;
    }

    /// <inheritdoc />
    public Task<string> EnqueueAsync(BackgroundTaskEnvelope envelope)
    {
        var payload = SerializeEnvelope(envelope);
        var queue = envelope.GetQueueName() ?? "default";

        var jobId = this._jobClient.Enqueue<HangfireBackgroundTaskExecutor>(
            queue,
            executor => executor.ExecuteAsync(payload, CancellationToken.None));

        return Task.FromResult(jobId);
    }

    /// <inheritdoc />
    public Task<string> ScheduleAsync(BackgroundTaskEnvelope envelope, TimeSpan delay)
    {
        var payload = SerializeEnvelope(envelope);

        var jobId = this._jobClient.Schedule<HangfireBackgroundTaskExecutor>(
            executor => executor.ExecuteAsync(payload, CancellationToken.None),
            delay);

        return Task.FromResult(jobId);
    }

    /// <inheritdoc />
    public Task<string> EnqueueChildAsync(
        BackgroundTaskEnvelope envelope,
        string parentId,
        BackgroundTaskContinuationOptions continuationOptions)
    {
        var payload = SerializeEnvelope(envelope);

        var hangfireOptions = continuationOptions switch
        {
            BackgroundTaskContinuationOptions.OnlyOnSucceededState => JobContinuationOptions.OnlyOnSucceededState,
            BackgroundTaskContinuationOptions.OnAnyFinishedState => JobContinuationOptions.OnAnyFinishedState,
            _ => JobContinuationOptions.OnlyOnSucceededState,
        };

        var jobId = this._jobClient.ContinueJobWith<HangfireBackgroundTaskExecutor>(
            parentId,
            executor => executor.ExecuteAsync(payload, CancellationToken.None),
            hangfireOptions);

        return Task.FromResult(jobId);
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
