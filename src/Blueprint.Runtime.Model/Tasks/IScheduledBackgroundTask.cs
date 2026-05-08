using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Represents a scheduled background task, allowing continuation tasks to be chained
/// fluently via <see cref="ContinueWith"/>.
/// </summary>
[PublicAPI]
public interface IScheduledBackgroundTask
{
    /// <summary>
    /// Adds a continuation task that will execute <em>after</em> this task completes, with
    /// options to control whether the continuation runs only on success (the default) or
    /// regardless of parent outcome.
    /// </summary>
    /// <param name="backgroundTask">The task to execute as a continuation.</param>
    /// <param name="options">Controls whether the continuation runs only on parent success or on any finish state.</param>
    /// <returns>A new <see cref="IScheduledBackgroundTask"/> representing the continuation, enabling further chaining.</returns>
    IScheduledBackgroundTask ContinueWith(
        IBackgroundTask backgroundTask,
        BackgroundTaskContinuationOptions options = BackgroundTaskContinuationOptions.OnlyOnSucceededState);
}
