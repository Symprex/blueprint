using System.Text.Json;

namespace Blueprint.Tasks.Hangfire;

/// <summary>
/// Wraps a <see cref="BackgroundTaskEnvelope"/> to provide a dashboard-friendly
/// <see cref="ToString"/> that shows the task type and its serialized properties,
/// matching Hangfire's <c>[DisplayName("{0}")]</c> convention.
/// </summary>
public sealed class HangfireBackgroundTaskWrapper
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HangfireBackgroundTaskWrapper"/> class.
    /// </summary>
    /// <param name="envelope">The task envelope to wrap.</param>
    public HangfireBackgroundTaskWrapper(BackgroundTaskEnvelope envelope)
    {
        this.Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
    }

    /// <summary>
    /// Gets the wrapped task envelope.
    /// </summary>
    public BackgroundTaskEnvelope Envelope { get; }

    /// <summary>
    /// Returns a dashboard-friendly representation: <c>TaskTypeName(property1: value1, ...)</c>.
    /// </summary>
    /// <returns>A string combining the task type name with its serialized properties.</returns>
    public override string ToString()
    {
        if (this.Envelope.Task is null)
        {
            return "[ERR] Unknown task";
        }

        try
        {
            var json = JsonSerializer.Serialize(this.Envelope.Task, this.Envelope.Task.GetType());

            // Strip outer braces for readability: {"Foo":"bar"} → "Foo":"bar"
            if (json.Length > 2)
            {
                json = json[1..^1];
            }

            return $"{this.Envelope.Task.GetType().Name}({json})";
        }
        catch
        {
            return this.Envelope.Task.GetType().Name;
        }
    }
}
