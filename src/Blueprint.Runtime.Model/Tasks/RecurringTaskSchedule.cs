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
        this.Id = name;
        this.BackgroundTask = backgroundTask;
        this.CronExpression = cronExpression;
        this.TimeZone = timeZone;
        this.Queue = queue;
    }

    /// <summary>
    /// Initialises a copy of <paramref name="schedule"/> registered under <paramref name="id"/>, for the recurring task
    /// manager to give each schedule it collects the id of the scheduler that declared it.
    /// </summary>
    /// <param name="schedule">The schedule a scheduler declared.</param>
    /// <param name="id">The id to register it under.</param>
    internal RecurringTaskSchedule(RecurringTaskSchedule schedule, string id)
        : this(schedule.Name, schedule.BackgroundTask, schedule.CronExpression, schedule.TimeZone, schedule.Queue)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        this.Id = id;
    }

    /// <summary>
    /// Gets the unique name of this recurring task schedule.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the id this schedule is registered under: <c>{SchedulerType}:{Name}</c> once the recurring task manager has
    /// collected it from the scheduler that declared it, and <see cref="Name"/> otherwise.
    /// </summary>
    /// <remarks>
    /// The id is legacy Blueprint's: its <c>RecurringTaskManager</c> joined the declaring scheduler's type name
    /// (<c>Type.Name</c>, so a nested scheduler contributes only its own name) and the schedule's name with a colon
    /// (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Tasks/RecurringTaskManager.cs:18</c>, <c>:70-73</c>, <c>:123-130</c>).
    /// A provider that registers each schedule under it keeps the jobs legacy registered, and lets two schedulers declare
    /// the same name. <see cref="Name"/> stays as the scheduler declared it.
    /// </remarks>
    public string Id { get; }

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
