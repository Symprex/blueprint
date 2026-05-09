using System.Diagnostics;
using Blueprint.Tasks;

namespace Blueprint.Runtime.Tasks;

/// <summary>
/// Production implementation of <see cref="IBackgroundTaskScheduler"/> that holds scheduled tasks
/// until the generated pipeline calls <see cref="IBackgroundTaskScheduler.RunNowAsync"/> in the
/// PostExecution stage, then dispatches them to the registered <see cref="IBackgroundTaskScheduleProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the unit-of-work pattern for background tasks: tasks are buffered during
/// handler execution and only released when the pipeline confirms success. If the handler fails,
/// no tasks are dispatched.
/// </para>
/// <para>
/// Register as <c>Scoped</c> so each operation gets its own task buffer. The generated pipeline's
/// BackgroundTaskFlush stage calls <see cref="IBackgroundTaskScheduler.RunNowAsync"/> automatically.
/// </para>
/// </remarks>
public sealed class BackgroundTaskScheduler : IBackgroundTaskScheduler
{
    private readonly IBackgroundTaskScheduleProvider _provider;
    private readonly IEnumerable<IBackgroundTaskPreprocessor> _preprocessors;
    private readonly ILogger<BackgroundTaskScheduler> _logger;

    private List<ScheduledBackgroundTask>? _tasks;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundTaskScheduler"/> class.
    /// </summary>
    /// <param name="provider">The provider-specific dispatch implementation.</param>
    /// <param name="preprocessors">Preprocessors that enrich task envelopes before dispatch.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public BackgroundTaskScheduler(
        IBackgroundTaskScheduleProvider provider,
        IEnumerable<IBackgroundTaskPreprocessor> preprocessors,
        ILogger<BackgroundTaskScheduler> logger)
    {
        this._provider = provider;
        this._preprocessors = preprocessors;
        this._logger = logger;
    }

    /// <inheritdoc />
    public IScheduledBackgroundTask Enqueue(IBackgroundTask task)
    {
        this._tasks ??= new List<ScheduledBackgroundTask>();

        var envelope = this.CreateEnvelope(task);
        var scheduled = new ScheduledBackgroundTask(envelope, delay: null, scheduler: this);

        this._tasks.Add(scheduled);

        return scheduled;
    }

    /// <inheritdoc />
    public IScheduledBackgroundTask Schedule(IBackgroundTask task, TimeSpan delay)
    {
        this._tasks ??= new List<ScheduledBackgroundTask>();

        var envelope = this.CreateEnvelope(task);
        var scheduled = new ScheduledBackgroundTask(envelope, delay, scheduler: this);

        this._tasks.Add(scheduled);

        return scheduled;
    }

    /// <inheritdoc />
    public ValueTask RunNowAsync()
    {
        if (this._tasks is not { Count: > 0 })
        {
            return default;
        }

        var current = new List<ScheduledBackgroundTask>(this._tasks);
        this._tasks.Clear();

        if (current.Count > 5)
        {
            this._logger.LogWarning(
                "Dispatching {TaskCount} background tasks in a single flush — consider consolidating to reduce scheduling overhead",
                current.Count);
        }

        return new ValueTask(this.DispatchAllAsync(current));
    }

    private async Task DispatchAllAsync(List<ScheduledBackgroundTask> tasks)
    {
        foreach (var task in tasks)
        {
            await task.PushToProviderAsync(this._provider);
        }
    }

    private BackgroundTaskEnvelope CreateEnvelope(IBackgroundTask task)
    {
        var envelope = new BackgroundTaskEnvelope(task);

        foreach (var preprocessor in this._preprocessors)
        {
            preprocessor.Preprocess(envelope);
        }

        return envelope;
    }

    /// <summary>
    /// Represents a root scheduled task that supports continuation chaining.
    /// </summary>
    [DebuggerDisplay("ScheduledBackgroundTask: {_envelope.Task.GetType().Name}")]
    internal sealed class ScheduledBackgroundTask : IScheduledBackgroundTask
    {
        private readonly BackgroundTaskEnvelope _envelope;
        private readonly TimeSpan? _delay;
        private readonly BackgroundTaskScheduler _scheduler;

        private List<ChildScheduledBackgroundTask>? _children;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScheduledBackgroundTask"/> class.
        /// </summary>
        /// <param name="envelope">The preprocessed task envelope.</param>
        /// <param name="delay">Optional delay before dispatch.</param>
        /// <param name="scheduler">The owning scheduler for creating child tasks.</param>
        internal ScheduledBackgroundTask(BackgroundTaskEnvelope envelope, TimeSpan? delay, BackgroundTaskScheduler scheduler)
        {
            this._envelope = envelope;
            this._delay = delay;
            this._scheduler = scheduler;
        }

        /// <inheritdoc />
        public IScheduledBackgroundTask ContinueWith(
            IBackgroundTask backgroundTask,
            BackgroundTaskContinuationOptions options = BackgroundTaskContinuationOptions.OnlyOnSucceededState)
        {
            this._children ??= new List<ChildScheduledBackgroundTask>();

            var childEnvelope = new BackgroundTaskEnvelope(backgroundTask)
            {
                Headers = this._envelope.Headers,
            };

            var child = new ChildScheduledBackgroundTask(childEnvelope, options, this._scheduler);
            this._children.Add(child);

            return child;
        }

        /// <summary>
        /// Dispatches this task and its children to the schedule provider.
        /// </summary>
        /// <param name="provider">The infrastructure provider to dispatch to.</param>
        /// <returns>A task representing the asynchronous dispatch operation.</returns>
        internal async Task PushToProviderAsync(IBackgroundTaskScheduleProvider provider)
        {
            string id;

            if (this._delay is null)
            {
                id = await provider.EnqueueAsync(this._envelope);
            }
            else
            {
                id = await provider.ScheduleAsync(this._envelope, this._delay.Value);
            }

            if (this._children is not null)
            {
                foreach (var child in this._children)
                {
                    await child.PushToProviderAsync(provider, id);
                }
            }
        }
    }

    /// <summary>
    /// A child task that depends on a parent task completing first.
    /// </summary>
    [DebuggerDisplay("ChildScheduledBackgroundTask: {_envelope.Task.GetType().Name}")]
    internal sealed class ChildScheduledBackgroundTask : IScheduledBackgroundTask
    {
        private readonly BackgroundTaskEnvelope _envelope;
        private readonly BackgroundTaskContinuationOptions _options;
        private readonly BackgroundTaskScheduler _scheduler;

        private List<ChildScheduledBackgroundTask>? _children;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChildScheduledBackgroundTask"/> class.
        /// </summary>
        /// <param name="envelope">The preprocessed task envelope.</param>
        /// <param name="options">Continuation options governing when this child runs.</param>
        /// <param name="scheduler">The owning scheduler for creating descendant tasks.</param>
        internal ChildScheduledBackgroundTask(
            BackgroundTaskEnvelope envelope,
            BackgroundTaskContinuationOptions options,
            BackgroundTaskScheduler scheduler)
        {
            this._envelope = envelope;
            this._options = options;
            this._scheduler = scheduler;
        }

        /// <inheritdoc />
        public IScheduledBackgroundTask ContinueWith(
            IBackgroundTask backgroundTask,
            BackgroundTaskContinuationOptions options = BackgroundTaskContinuationOptions.OnlyOnSucceededState)
        {
            this._children ??= new List<ChildScheduledBackgroundTask>();

            var childEnvelope = new BackgroundTaskEnvelope(backgroundTask)
            {
                Headers = this._envelope.Headers,
            };

            var child = new ChildScheduledBackgroundTask(childEnvelope, options, this._scheduler);
            this._children.Add(child);

            return child;
        }

        /// <summary>
        /// Dispatches this child task and its descendants to the schedule provider.
        /// </summary>
        /// <param name="provider">The infrastructure provider to dispatch to.</param>
        /// <param name="parentId">The provider-assigned ID of the parent task for continuation linkage.</param>
        /// <returns>A task representing the asynchronous dispatch operation.</returns>
        internal async Task PushToProviderAsync(IBackgroundTaskScheduleProvider provider, string parentId)
        {
            var id = await provider.EnqueueChildAsync(this._envelope, parentId, this._options);

            if (this._children is not null)
            {
                foreach (var child in this._children)
                {
                    await child.PushToProviderAsync(provider, id);
                }
            }
        }
    }
}
