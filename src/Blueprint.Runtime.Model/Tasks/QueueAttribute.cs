namespace Blueprint.Tasks;

/// <summary>
/// Specifies the queue name that a background task should be dispatched to.
/// The <see cref="IBackgroundTaskScheduleProvider"/> reads this attribute from the task type
/// to route to the correct infrastructure queue (e.g., Hangfire's named queues).
/// </summary>
/// <remarks>
/// <para>
/// When a task type is decorated with <c>[Queue("portal")]</c>, providers should honour
/// that routing instruction. If no attribute is present, providers use their default queue.
/// This is the compile-time equivalent of legacy Blueprint's queue routing convention.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Queue("email-sending")]
/// public partial class SendWelcomeEmailTask : IBackgroundTask
/// {
///     public required string UserId { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class QueueAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueueAttribute"/> class.
    /// </summary>
    /// <param name="name">The name of the queue to dispatch the task to.</param>
    public QueueAttribute(string name)
    {
        this.Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the queue name that this task should be dispatched to.
    /// </summary>
    public string Name { get; }
}
