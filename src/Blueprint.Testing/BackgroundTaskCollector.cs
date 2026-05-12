using Blueprint.Tasks;

namespace Blueprint.Testing;

/// <summary>
/// A test-only <see cref="IBackgroundTaskScheduleProvider"/> that captures all enqueued and scheduled
/// tasks for later assertion without dispatching them to any infrastructure. Replace the real
/// provider with this in test DI to verify handler task scheduling behaviour.
/// </summary>
/// <remarks>
/// <para>
/// This class mirrors the S365 <c>BackgroundTaskCollector</c> pattern. Tasks are captured
/// into public lists so tests can assert on them directly.
/// </para>
/// </remarks>
public sealed class BackgroundTaskCollector : IBackgroundTaskScheduleProvider
{
    private readonly object _lock = new();
    private int _nextId;

    /// <summary>
    /// Gets the list of tasks enqueued via <see cref="EnqueueAsync"/>.
    /// </summary>
    public IReadOnlyList<BackgroundTaskEnvelope> Enqueued => this._enqueued;

    /// <summary>
    /// Gets the list of tasks scheduled via <see cref="ScheduleAsync"/> with their delays.
    /// </summary>
    public IReadOnlyList<(BackgroundTaskEnvelope Envelope, TimeSpan Delay)> Scheduled => this._scheduled;

    /// <summary>
    /// Gets the list of child tasks enqueued via <see cref="EnqueueChildAsync"/> with their parent IDs.
    /// </summary>
    public IReadOnlyList<(BackgroundTaskEnvelope Envelope, string ParentId, BackgroundTaskContinuationOptions Options)> ChildEnqueued
        => this._childEnqueued;

    /// <summary>
    /// Gets all collected tasks (enqueued + scheduled + child) as a combined sequence.
    /// Mirrors the S365 <c>BackgroundTaskCollector.All</c> property.
    /// </summary>
    public IEnumerable<object> All =>
        this._enqueued.Select(static e => (object)e.Task)
            .Concat(this._scheduled.Select(static s => (object)s.Envelope.Task))
            .Concat(this._childEnqueued.Select(static c => (object)c.Envelope.Task));

    private readonly List<BackgroundTaskEnvelope> _enqueued = new();
    private readonly List<(BackgroundTaskEnvelope Envelope, TimeSpan Delay)> _scheduled = new();
    private readonly List<(BackgroundTaskEnvelope Envelope, string ParentId, BackgroundTaskContinuationOptions Options)> _childEnqueued = new();

    /// <inheritdoc />
    public Task<string> EnqueueAsync(BackgroundTaskEnvelope task)
    {
        lock (this._lock)
        {
            this._enqueued.Add(task);
            return Task.FromResult($"test-job-{Interlocked.Increment(ref this._nextId)}");
        }
    }

    /// <inheritdoc />
    public Task<string> ScheduleAsync(BackgroundTaskEnvelope task, TimeSpan delay)
    {
        lock (this._lock)
        {
            this._scheduled.Add((task, delay));
            return Task.FromResult($"test-job-{Interlocked.Increment(ref this._nextId)}");
        }
    }

    /// <inheritdoc />
    public Task<string> EnqueueChildAsync(
        BackgroundTaskEnvelope task,
        string parentId,
        BackgroundTaskContinuationOptions continuationOptions)
    {
        lock (this._lock)
        {
            this._childEnqueued.Add((task, parentId, continuationOptions));
            return Task.FromResult($"test-job-{Interlocked.Increment(ref this._nextId)}");
        }
    }

    /// <summary>
    /// Gets a single enqueued task of the specified type, asserting exactly one exists.
    /// </summary>
    /// <typeparam name="T">The expected task type.</typeparam>
    /// <returns>The task instance.</returns>
    public T GetSingleEnqueued<T>() where T : IBackgroundTask
    {
        var matches = this._enqueued
            .Where(static e => e.Task is T)
            .Select(static e => (T)e.Task)
            .ToList();

        return matches.Count switch
        {
            0 => throw new InvalidOperationException(
                $"Expected a single enqueued task of type {typeof(T).Name} but none were enqueued. " +
                $"Enqueued types: {FormatTaskTypes(this._enqueued)}"),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Expected a single enqueued task of type {typeof(T).Name} but found {matches.Count}."),
        };
    }

    /// <summary>
    /// Gets all enqueued tasks of the specified type.
    /// </summary>
    /// <typeparam name="T">The task type to filter by.</typeparam>
    /// <returns>All matching task instances.</returns>
    public IReadOnlyList<T> GetAllEnqueued<T>() where T : IBackgroundTask
    {
        return this._enqueued
            .Where(static e => e.Task is T)
            .Select(static e => (T)e.Task)
            .ToList();
    }

    /// <summary>
    /// Asserts that no tasks of the specified type have been enqueued.
    /// </summary>
    /// <typeparam name="T">The task type that should not be present.</typeparam>
    public void AssertNoneEnqueued<T>() where T : IBackgroundTask
    {
        var matches = this._enqueued.Where(static e => e.Task is T).ToList();

        if (matches.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no enqueued tasks of type {typeof(T).Name} but found {matches.Count}.");
        }
    }

    /// <summary>
    /// Clears all collected tasks. Useful between test executions in shared fixture scenarios.
    /// </summary>
    public void ClearAll()
    {
        lock (this._lock)
        {
            this._enqueued.Clear();
            this._scheduled.Clear();
            this._childEnqueued.Clear();
        }
    }

    private static string FormatTaskTypes(List<BackgroundTaskEnvelope> envelopes)
    {
        if (envelopes.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", envelopes.Select(static e => e.Task.GetType().Name).Distinct());
    }
}
