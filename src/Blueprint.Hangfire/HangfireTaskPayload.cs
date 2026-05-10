namespace Blueprint.Hangfire;

/// <summary>
/// Internal payload structure serialized by <see cref="HangfireBackgroundTaskScheduleProvider"/>
/// and deserialized by <see cref="HangfireBackgroundTaskExecutor"/>. Contains the type name,
/// serialized task JSON, and any preprocessor headers.
/// </summary>
internal sealed class HangfireTaskPayload
{
    /// <summary>
    /// Gets or sets the assembly-qualified type name of the background task.
    /// </summary>
    public string TaskTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON-serialized task body.
    /// </summary>
    public string TaskJson { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the preprocessor headers (e.g. traceparent for distributed tracing).
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }
}
