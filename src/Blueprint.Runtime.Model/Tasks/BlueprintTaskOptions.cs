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
    /// Defaults to <c>"default"</c>.
    /// </summary>
    public string DefaultQueue { get; set; } = "default";

    /// <summary>
    /// Gets or sets the default number of automatic retry attempts for failed background tasks.
    /// Providers should honour this as a baseline unless overridden by task-specific configuration.
    /// Defaults to <c>5</c> (matching Hangfire's default).
    /// </summary>
    public int DefaultRetryCount { get; set; } = 5;
}
