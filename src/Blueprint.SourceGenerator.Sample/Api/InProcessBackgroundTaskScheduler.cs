using Blueprint.Tasks;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A simple in-process implementation of <see cref="IBackgroundTaskScheduler"/> that logs enqueued tasks
/// and executes them inline during flush. Production consumers would typically replace this with a
/// durable scheduler (Hangfire, Azure Service Bus, etc.) that dispatches to the background-task host.
/// </summary>
public sealed class InProcessBackgroundTaskScheduler(ILogger<InProcessBackgroundTaskScheduler> logger) : IBackgroundTaskScheduler
{
    private readonly List<object> _pending = [];

    /// <inheritdoc />
    public void Enqueue(IBackgroundTask task)
    {
        logger.LogInformation("Background task enqueued: {TaskType}", task.GetType().Name);
        this._pending.Add(task);
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (this._pending.Count == 0)
        {
            return Task.CompletedTask;
        }

        logger.LogInformation("Flushing {Count} background task(s)", this._pending.Count);
        this._pending.Clear();

        return Task.CompletedTask;
    }
}
