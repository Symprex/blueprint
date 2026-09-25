using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Configuration options for the Blueprint background task infrastructure.
/// Registered via <c>IOptions&lt;BlueprintTaskOptions&gt;</c> and read by providers
/// and the task executor.
/// </summary>
[PublicAPI]
public sealed class BlueprintTaskOptions
{
    /// <summary>
    /// Gets or sets the default queue name used by <see cref="IBackgroundTaskScheduleProvider"/>
    /// implementations when a task type does not specify a <see cref="QueueAttribute"/>.
    /// Defaults to <see langword="null"/>, for which the providers use Hangfire's <c>"default"</c> queue.
    /// </summary>
    /// <remarks>
    /// Unset by default, as legacy Blueprint's was (<c>Blueprint.Tasks/BlueprintTaskOptions.cs:10-11</c> at Signature365
    /// <c>8b40ecc961</c>), because the value itself is part of an id: the Hangfire recurring task provider's scheduler job
    /// is <c>{DefaultQueue}:Scheduler</c>, which with no default queue is legacy's <c>:Scheduler</c>, while the job still
    /// runs on the <c>"default"</c> queue.
    /// </remarks>
    public string? DefaultQueue { get; set; }

    /// <summary>
    /// Gets or sets the default number of automatic retry attempts for failed background tasks.
    /// Providers should honour this as a baseline unless overridden by task-specific configuration.
    /// Defaults to <c>5</c> (matching Hangfire's default).
    /// </summary>
    public int DefaultRetryCount { get; set; } = 5;
}
