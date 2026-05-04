using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Abstraction for enqueueing background tasks during handler execution and flushing them
/// after the handler completes successfully. Implementations may dispatch immediately,
/// queue to Hangfire, or use any other scheduling mechanism.
/// </summary>
/// <remarks>
/// <para>
/// Handlers receive this via DI (typically scoped per-operation). Tasks enqueued during handler
/// execution are held until the generated pipeline's PostExecution stage calls <see cref="FlushAsync"/>.
/// This ensures tasks are only dispatched when the handler succeeds.
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
    /// <see cref="FlushAsync"/> is called by the generated pipeline after successful handler execution.
    /// </summary>
    /// <param name="task">The background task to schedule.</param>
    void Enqueue(IBackgroundTask task);

    /// <summary>
    /// Dispatches all previously enqueued tasks to the underlying scheduling mechanism.
    /// Called by the generated pipeline in the PostExecution stage after verifying handler success.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the flush operation.</param>
    /// <returns>A task representing the asynchronous flush operation.</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
