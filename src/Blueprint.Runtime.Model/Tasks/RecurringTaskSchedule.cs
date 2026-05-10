using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Represents a single recurring schedule for a <see cref="IBackgroundTask" />, a task that
/// on a given time cadence will be executed by the background task processor.
/// </summary>
[PublicAPI]
public sealed class RecurringTaskSchedule
{
    /// <summary>
    /// Initialises a new instance of the <see cref="RecurringTaskSchedule" /> class in the <see cref="TimeZoneInfo.Utc" />
    /// time zone.
    /// </summary>
    /// <param name="name">The (unique) name of this schedule.</param>
    /// <param name="backgroundTask">The task that should be executed.</param>
    /// <param name="cronExpression">A CRON expression representing when the task should be executed.</param>
    public RecurringTaskSchedule(string name, IBackgroundTask backgroundTask, string cronExpression)
        : this(name, backgroundTask, cronExpression, TimeZoneInfo.Utc)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="RecurringTaskSchedule" /> class.
    /// </summary>
    /// <param name="name">The (unique) name of this schedule.</param>
    /// <param name="backgroundTask">The task that should be executed.</param>
    /// <param name="cronExpression">A CRON expression representing when the task should be executed.</param>
    /// <param name="timeZone">The time zone the CRON expression is in.</param>
    /// <param name="queue">An optional queue name to dispatch the task to.</param>
    public RecurringTaskSchedule(
        string name,
        IBackgroundTask backgroundTask,
        string cronExpression,
        TimeZoneInfo timeZone,
        string? queue = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(backgroundTask);
        ArgumentException.ThrowIfNullOrEmpty(cronExpression);

        this.Name = name;
        this.BackgroundTask = backgroundTask;
        this.CronExpression = cronExpression;
        this.TimeZone = timeZone;
        this.Queue = queue;
    }

    /// <summary>
    /// Gets the unique name of this recurring task schedule.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the background task to execute on the recurring schedule.
    /// </summary>
    public IBackgroundTask BackgroundTask { get; }

    /// <summary>
    /// Gets the CRON expression describing the execution schedule.
    /// </summary>
    public string CronExpression { get; }

    /// <summary>
    /// Gets the time zone the CRON expression will be interpreted in.
    /// </summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>
    /// Gets the optional queue name to dispatch the task to. When <c>null</c>,
    /// the provider's default queue is used.
    /// </summary>
    public string? Queue { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{this.Name}: {this.BackgroundTask.GetType().Name} ({this.CronExpression} in {this.TimeZone.Id})";
    }
}
