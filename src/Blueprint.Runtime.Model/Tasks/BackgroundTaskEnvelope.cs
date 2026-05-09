using System.Reflection;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Wraps an <see cref="IBackgroundTask"/> with transport metadata (headers) that travel
/// alongside the task through serialization and dispatch. Headers carry distributed trace
/// context and other ambient information that the task executor uses to
/// correlate background execution with the originating request.
/// </summary>
[PublicAPI]
public sealed class BackgroundTaskEnvelope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundTaskEnvelope"/> class wrapping the specified task.
    /// </summary>
    /// <param name="task">The background task to wrap.</param>
    public BackgroundTaskEnvelope(IBackgroundTask task)
    {
        this.Task = task ?? throw new ArgumentNullException(nameof(task));
    }

    /// <summary>
    /// Gets the <see cref="IBackgroundTask"/> this envelope wraps.
    /// </summary>
    public IBackgroundTask Task { get; }

    /// <summary>
    /// Gets or sets the metadata headers that travel with the task through serialization.
    /// Typically contains W3C Trace Context propagation entries (traceparent, tracestate)
    /// injected by an <see cref="IBackgroundTaskPreprocessor"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets the queue name specified by the <see cref="QueueAttribute"/> on the task type,
    /// or <c>null</c> if no queue is explicitly specified (provider uses its default).
    /// </summary>
    /// <returns>The queue name, or <c>null</c> for the default queue.</returns>
    public string? GetQueueName()
    {
        return this.Task.GetType().GetCustomAttribute<QueueAttribute>()?.Name;
    }

    /// <summary>
    /// Gets the retry attempt count specified by the <see cref="AutomaticRetryAttribute"/> on the task type,
    /// or <c>null</c> if no explicit retry configuration is present (provider uses its default from options).
    /// </summary>
    /// <returns>The maximum retry attempts, or <c>null</c> for the default from <see cref="BlueprintTaskOptions"/>.</returns>
    public int? GetRetryAttempts()
    {
        return this.Task.GetType().GetCustomAttribute<AutomaticRetryAttribute>()?.Attempts;
    }
}
