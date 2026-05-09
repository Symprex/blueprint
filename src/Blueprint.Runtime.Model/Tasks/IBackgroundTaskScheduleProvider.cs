using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// The provider-specific integration point that performs actual background task dispatch.
/// Consumers implement this interface to bridge Blueprint's task scheduling to concrete
/// infrastructure such as Hangfire, Azure Queue Storage, or an in-process dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// The production background task scheduler collects tasks during handler execution and
/// delegates to this provider when the generated pipeline calls <see cref="IBackgroundTaskScheduler.RunNowAsync"/>.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBackgroundTaskScheduleProvider
{
    /// <summary>
    /// Enqueues a task for immediate background execution.
    /// </summary>
    /// <param name="task">The envelope containing the task and its metadata.</param>
    /// <returns>An opaque identifier for the created background job.</returns>
    Task<string> EnqueueAsync(BackgroundTaskEnvelope task);

    /// <summary>
    /// Schedules a task for background execution after the specified delay.
    /// </summary>
    /// <param name="task">The envelope containing the task and its metadata.</param>
    /// <param name="delay">How long to wait before executing the task.</param>
    /// <returns>An opaque identifier for the created background job.</returns>
    Task<string> ScheduleAsync(BackgroundTaskEnvelope task, TimeSpan delay);

    /// <summary>
    /// Enqueues a child task that depends on a parent task completing first.
    /// </summary>
    /// <param name="task">The envelope containing the task and its metadata.</param>
    /// <param name="parentId">The identifier of the parent task (returned from a prior Enqueue/Schedule call).</param>
    /// <param name="continuationOptions">Controls when the child task runs relative to its parent's outcome.</param>
    /// <returns>An opaque identifier for the created child job.</returns>
    Task<string> EnqueueChildAsync(BackgroundTaskEnvelope task, string parentId, BackgroundTaskContinuationOptions continuationOptions);
}
