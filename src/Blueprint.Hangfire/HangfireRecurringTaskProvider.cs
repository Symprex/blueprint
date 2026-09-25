using System.Text.Json;
using Blueprint.Runtime.Tasks;
using Blueprint.Tasks;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CS4014 // Hangfire expression trees — not awaited by our code

namespace Blueprint.Hangfire;

/// <summary>
/// Implements <see cref="IRecurringTaskProvider"/> using Hangfire's
/// <see cref="IRecurringJobManager"/> for persistent cron-based scheduling.
/// </summary>
/// <remarks>
/// <para>
/// On each call to <see cref="RegisterSchedulesAsync"/>, this provider adds or updates
/// every schedule in the supplied list using Hangfire's <c>AddOrUpdate</c>, then removes
/// the recurring jobs it created earlier whose schedules are no longer present, so that
/// schedules removed from code are also removed from Hangfire. Recurring jobs it did not
/// create are never removed.
/// </para>
/// <para>
/// <c>{DefaultQueue}:Scheduler</c> is a recurring job that re-runs <see cref="RecurringTaskManager.RescheduleAllAsync"/>
/// every 30 minutes, as legacy Blueprint's did. The host's start-up installs it through
/// <see cref="SetupRecurringManagerAsync"/> and does nothing else, as legacy's start-up did, so the schedules are registered
/// only when that job runs. Each registration installs it again too.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HangfireRecurringTaskProvider : IPeriodicRecurringTaskProvider
{
    /// <summary>
    /// Prefix applied to all Hangfire recurring job IDs created by Blueprint.
    /// Prevents collisions with non-Blueprint recurring jobs and enables orphan detection.
    /// </summary>
    internal const string JobIdPrefix = "blueprint:";

    /// <summary>
    /// Appended to the app's default queue to form the id of the job that re-runs registration, legacy's
    /// <c>{DefaultQueue}:Scheduler</c>.
    /// </summary>
    internal const string RescheduleJobIdSuffix = ":Scheduler";

    /// <summary>
    /// How often the re-run fires: every 30 minutes, as legacy's did.
    /// </summary>
    internal const string RescheduleCron = "*/30 * * * *";

    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<HangfireRecurringTaskProvider> _logger;
    private readonly string _defaultQueue;
    private readonly string _schedulerJobId;
    private readonly JobStorage? _storage;

    /// <summary>
    /// Initialises the provider with the Hangfire recurring job manager.
    /// </summary>
    /// <param name="recurringJobManager">Hangfire's recurring job manager.</param>
    /// <param name="logger">Logger for registration diagnostics.</param>
    public HangfireRecurringTaskProvider(
        IRecurringJobManager recurringJobManager,
        ILogger<HangfireRecurringTaskProvider> logger)
        : this(recurringJobManager, Options.Create(new BlueprintTaskOptions()), logger)
    {
    }

    /// <summary>
    /// Initialises the provider with the Hangfire recurring job manager and the app's task options, whose
    /// <see cref="BlueprintTaskOptions.DefaultQueue"/> receives every schedule that names no queue of its own.
    /// </summary>
    /// <param name="recurringJobManager">Hangfire's recurring job manager.</param>
    /// <param name="taskOptions">The app's background task options.</param>
    /// <param name="logger">Logger for registration diagnostics.</param>
    public HangfireRecurringTaskProvider(
        IRecurringJobManager recurringJobManager,
        IOptions<BlueprintTaskOptions> taskOptions,
        ILogger<HangfireRecurringTaskProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(recurringJobManager);
        ArgumentNullException.ThrowIfNull(taskOptions);

        this._recurringJobManager = recurringJobManager;
        this._logger = logger;
        this._defaultQueue = taskOptions.Value.DefaultQueue ?? EnqueuedState.DefaultQueue;

        // From the option as set, not the queue the job runs on: with no default queue, legacy's id was ":Scheduler"
        // (legacy Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:45-46).
        this._schedulerJobId = taskOptions.Value.DefaultQueue + RescheduleJobIdSuffix;
    }

    /// <summary>
    /// Initialises the provider with the Hangfire recurring job manager, the storage it writes to, and the app's task
    /// options. The storage is where the provider looks for the recurring jobs it created that are no longer declared.
    /// </summary>
    /// <remarks>
    /// The other constructors read <see cref="JobStorage.Current"/> instead, when they look.
    /// </remarks>
    /// <param name="recurringJobManager">Hangfire's recurring job manager.</param>
    /// <param name="storage">The Hangfire storage <paramref name="recurringJobManager"/> writes to.</param>
    /// <param name="taskOptions">The app's background task options.</param>
    /// <param name="logger">Logger for registration diagnostics.</param>
    public HangfireRecurringTaskProvider(
        IRecurringJobManager recurringJobManager,
        JobStorage storage,
        IOptions<BlueprintTaskOptions> taskOptions,
        ILogger<HangfireRecurringTaskProvider> logger)
        : this(recurringJobManager, taskOptions, logger)
    {
        ArgumentNullException.ThrowIfNull(storage);

        this._storage = storage;
    }

    /// <inheritdoc />
    public Task RegisterSchedulesAsync(
        IReadOnlyList<RecurringTaskSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        // First, so that a registration that fails part-way still leaves the job that retries it.
        this.InstallRescheduleJob();

        var registeredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var schedule in schedules)
        {
            var jobId = JobIdPrefix + schedule.Name;
            registeredIds.Add(jobId);

            var envelope = new BackgroundTaskEnvelope(schedule.BackgroundTask);
            var payload = JsonSerializer.Serialize(new HangfireTaskPayload
            {
                TaskTypeName = schedule.BackgroundTask.GetType().AssemblyQualifiedName!,
                TaskJson = JsonSerializer.Serialize(
                    schedule.BackgroundTask,
                    schedule.BackgroundTask.GetType()),
            });

            var queue = schedule.Queue ?? envelope.GetQueueName() ?? this._defaultQueue;

            this._recurringJobManager.AddOrUpdate<HangfireBackgroundTaskExecutor>(
                jobId,
                queue,
                executor => executor.ExecuteAsync(payload, CancellationToken.None),
                schedule.CronExpression,
                new RecurringJobOptions { TimeZone = schedule.TimeZone });

            this._logger.LogInformation(
                "Registered recurring task {ScheduleName} ({TaskType}) with cron '{Cron}' on queue '{Queue}'",
                schedule.Name,
                schedule.BackgroundTask.GetType().Name,
                schedule.CronExpression,
                queue);
        }

        // Only once every declared schedule is registered, so a registration that fails part-way removes nothing.
        this.RemoveStaleJobs(registeredIds);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Installs <c>{DefaultQueue}:Scheduler</c> and nothing else, as legacy's <c>SetupRecurringManagerAsync</c> did
    /// (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:110-127</c>).
    /// </remarks>
    public Task SetupRecurringManagerAsync(CancellationToken cancellationToken = default)
    {
        this.InstallRescheduleJob();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds or updates the recurring job that re-runs <see cref="RecurringTaskManager.RescheduleAllAsync"/> every 30 minutes
    /// on the app's default queue, as legacy Blueprint's provider did in <c>SetupRecurringManagerAsync</c>: the same id
    /// (<c>{DefaultQueue}:Scheduler</c>, so <c>:Scheduler</c> when no default queue is set), the same cron
    /// (<c>*/30 * * * *</c>) and the same queue (Hangfire's <c>default</c> when none is set).
    /// </summary>
    /// <remarks>
    /// Its id does not carry <see cref="JobIdPrefix"/>, so stale-job removal never removes it. It is installed as part of
    /// registration, so it exists only where the app's schedules are registered, which is where legacy installed it. The
    /// job activator must be able to resolve <see cref="RecurringTaskManager"/>, which <c>AddRecurringTaskProvider</c>
    /// registers.
    /// </remarks>
    private void InstallRescheduleJob()
    {
        this._recurringJobManager.AddOrUpdate<RecurringTaskManager>(
            this._schedulerJobId,
            this._defaultQueue,
            manager => manager.RescheduleAllAsync(CancellationToken.None),
            RescheduleCron,
            new RecurringJobOptions());
    }

    /// <summary>
    /// Removes each recurring job in the storage that this provider created — its id carries <see cref="JobIdPrefix"/> —
    /// and whose schedule is not among <paramref name="declaredIds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The set of recurring jobs is read from the storage, which is what scopes removal to one app: legacy Blueprint
    /// removed every undeclared recurring job its storage listed, and Signature365 partitions that list by app. Within it,
    /// a job whose id this provider did not create — legacy's <c>{scheduler}:{name}</c> ids, or any other code's — is left
    /// alone. The supplied schedules are therefore taken to be the app's complete set, so the provider belongs only in the
    /// process that registers every one of the app's recurring task schedulers.
    /// </para>
    /// <para>
    /// A declared id is matched case-insensitively, as Hangfire's SQL storage matches it, so a stored id differing only in
    /// case is the job just registered and is kept. Failing to read the storage is logged rather than thrown: the declared
    /// schedules are registered by then, and a stale job that survives is the safe side of that failure.
    /// </para>
    /// </remarks>
    /// <param name="declaredIds">The ids of the recurring jobs just registered.</param>
    private void RemoveStaleJobs(HashSet<string> declaredIds)
    {
        List<string> staleIds;

        try
        {
            using var connection = (this._storage ?? JobStorage.Current).GetConnection();

            staleIds = connection.GetRecurringJobs()
                .Select(static job => job.Id)
                .Where(id => id.StartsWith(JobIdPrefix, StringComparison.Ordinal) && !declaredIds.Contains(id))
                .ToList();
        }
        catch (Exception e)
        {
            this._logger.LogError(
                e,
                "Could not read the recurring jobs in Hangfire storage, so recurring tasks that are no longer declared have not been removed");

            return;
        }

        foreach (var staleId in staleIds)
        {
            this._recurringJobManager.RemoveIfExists(staleId);

            this._logger.LogInformation(
                "Removed recurring task {JobId}, which no recurring task scheduler declares any longer",
                staleId);
        }
    }
}
