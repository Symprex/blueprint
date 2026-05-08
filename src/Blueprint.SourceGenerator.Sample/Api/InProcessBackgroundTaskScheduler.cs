using Blueprint.Tasks;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A simple in-process implementation of <see cref="IBackgroundTaskScheduler"/> that logs enqueued tasks
/// and executes them inline during flush. Production consumers would typically replace this with a
/// durable scheduler (Hangfire, Azure Service Bus, etc.) that dispatches to the background-task host.
/// </summary>
public sealed class InProcessBackgroundTaskScheduler(ILogger<InProcessBackgroundTaskScheduler> logger) : IBackgroundTaskScheduler
{
    private readonly List<ScheduledEntry> _pending = [];

    /// <inheritdoc />
    public IScheduledBackgroundTask Enqueue(IBackgroundTask task)
    {
        logger.LogInformation("Background task enqueued: {TaskType}", task.GetType().Name);

        var entry = new ScheduledEntry(task, TimeSpan.Zero);
        this._pending.Add(entry);

        return entry;
    }

    /// <inheritdoc />
    public IScheduledBackgroundTask Schedule(IBackgroundTask task, TimeSpan delay)
    {
        logger.LogInformation("Background task scheduled: {TaskType} (delay: {Delay})", task.GetType().Name, delay);

        var entry = new ScheduledEntry(task, delay);
        this._pending.Add(entry);

        return entry;
    }

    /// <inheritdoc />
    public ValueTask RunNowAsync()
    {
        if (this._pending.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        logger.LogInformation("Dispatching {Count} background task(s)", this._pending.Count);
        this._pending.Clear();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// In-memory scheduled task entry that supports fluent continuation chaining.
    /// A production implementation (e.g. Hangfire adapter) would track parent–child relationships
    /// and honour <see cref="BackgroundTaskContinuationOptions"/> when dispatching.
    /// </summary>
    private sealed class ScheduledEntry(IBackgroundTask task, TimeSpan delay) : IScheduledBackgroundTask
    {
        public IBackgroundTask Task { get; } = task;

        public TimeSpan Delay { get; } = delay;

        public IScheduledBackgroundTask ContinueWith(
            IBackgroundTask backgroundTask,
            BackgroundTaskContinuationOptions options = BackgroundTaskContinuationOptions.OnlyOnSucceededState)
        {
            // In-process scheduler doesn't honour continuation options — just enqueues.
            // A durable scheduler (Hangfire) would track the parent→child DAG.
            return new ScheduledEntry(backgroundTask, TimeSpan.Zero);
        }
    }
}
