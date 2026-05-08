using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Controls whether a continuation task (added via <see cref="IScheduledBackgroundTask.ContinueWith"/>)
/// should run based on the parent task's outcome.
/// </summary>
[PublicAPI]
public enum BackgroundTaskContinuationOptions
{
    /// <summary>
    /// The continuation task executes regardless of whether the parent task succeeded or failed.
    /// </summary>
    OnAnyFinishedState,

    /// <summary>
    /// The continuation task executes only if the parent task completed successfully
    /// (including after retries that eventually succeed).
    /// </summary>
    OnlyOnSucceededState,
}
