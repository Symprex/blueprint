using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Abstraction for enqueueing and scheduling background tasks during handler execution.
/// Tasks are held until the generated pipeline calls <see cref="RunNowAsync"/> in the
/// PostExecution stage, ensuring tasks are only dispatched when the handler succeeds.
/// </summary>
/// <remarks>
/// <para>
/// Handlers receive this via DI (typically scoped per-operation). Tasks enqueued or scheduled
/// during handler execution are held until <see cref="RunNowAsync"/> releases them.
/// </para>
/// <para>
/// Both <see cref="Enqueue"/> and <see cref="Schedule"/> return an <see cref="IScheduledBackgroundTask"/>
/// that supports fluent continuation chaining via <see cref="IScheduledBackgroundTask.ContinueWith"/>.
/// </para>
/// <para>
/// Concrete implementations live in optional packages (e.g. Hangfire integration) or in the
/// consuming app's DI configuration for in-process dispatch.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBackgroundTaskScheduler
{
    /// <summary>
    /// Enqueues a background task for deferred execution. The task is held until
    /// <see cref="RunNowAsync"/> is called by the generated pipeline after successful handler execution.
    /// </summary>
    /// <param name="task">The background task to schedule.</param>
    /// <returns>A scheduled task handle that supports fluent continuation chaining.</returns>
    IScheduledBackgroundTask Enqueue(IBackgroundTask task);

    /// <summary>
    /// Schedules a background task for deferred execution with a delay. The task is held until
    /// <see cref="RunNowAsync"/> is called, at which point it is dispatched with the specified delay.
    /// </summary>
    /// <param name="task">The background task to schedule.</param>
    /// <param name="delay">The amount of time to wait before executing the task.</param>
    /// <returns>A scheduled task handle that supports fluent continuation chaining.</returns>
    IScheduledBackgroundTask Schedule(IBackgroundTask task, TimeSpan delay);

    /// <summary>
    /// Dispatches all previously enqueued and scheduled tasks to the underlying scheduling mechanism.
    /// Called by the generated pipeline in the PostExecution stage after verifying handler success.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispatch operation.</returns>
    ValueTask RunNowAsync();
}
