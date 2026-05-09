using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Specifies the automatic retry configuration for a background task. The underlying task
/// infrastructure (e.g., Hangfire) reads this attribute to configure retries when the task
/// fails with an unhandled exception.
/// </summary>
/// <remarks>
/// <para>
/// Apply this to a class implementing <see cref="IBackgroundTask"/> to override the default
/// retry behaviour configured via <see cref="BlueprintTaskOptions.DefaultRetryCount"/>.
/// </para>
/// <para>
/// When no attribute is present, the provider falls back to <see cref="BlueprintTaskOptions.DefaultRetryCount"/>.
/// Use <c>[AutomaticRetry(0)]</c> to disable retries for a specific task.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AutomaticRetry(3)]
/// [Queue("emails")]
/// public partial class SendWelcomeEmailTask : IBackgroundTask
/// {
///     public string RecipientEmail { get; set; }
/// }
/// </code>
/// </example>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AutomaticRetryAttribute(int attempts) : Attribute
{
    /// <summary>
    /// Gets the maximum number of retry attempts before the task is moved to the failed state.
    /// A value of 0 means no retries — the task fails immediately on first exception.
    /// </summary>
    public int Attempts { get; } = attempts;
}
