using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blueprint.Tasks.Hangfire;

/// <summary>
/// An <see cref="IRecurringTaskProvider"/> that registers recurring task schedules with Hangfire's
/// <see cref="IRecurringJobManager"/>. Automatically cleans up schedules that are no longer declared
/// by any <see cref="IRecurringTaskScheduler"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not safe for a host that runs more than one replica, or shares its Hangfire storage.</b> This provider is not an
/// <see cref="IPeriodicRecurringTaskProvider"/>, so every host start registers the full set of schedules and then removes
/// every recurring job in storage the set does not contain, with no filter on id: another app's jobs, other code's, and
/// the jobs another replica has just registered from live data. A scheduler that fails at start-up is counted as
/// declaring nothing, so its jobs are removed. Use <c>Blueprint.Hangfire</c>'s provider, which registers from a single
/// recurring scheduler job instead, for any such host.
/// </para>
/// <para>
/// Jobs are registered under <see cref="RecurringTaskSchedule.Name"/>, not <see cref="RecurringTaskSchedule.Id"/>, so two
/// schedulers declaring the same name share one job.
/// </para>
/// </remarks>
public sealed class HangfireRecurringTaskProvider : IRecurringTaskProvider
{
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<HangfireRecurringTaskProvider> _logger;
    private readonly string _defaultQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="HangfireRecurringTaskProvider"/> class.
    /// </summary>
    /// <param name="recurringJobManager">The Hangfire recurring job manager.</param>
    /// <param name="taskOptions">Blueprint task options containing the default queue name.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public HangfireRecurringTaskProvider(
        IRecurringJobManager recurringJobManager,
        IOptions<BlueprintTaskOptions> taskOptions,
        ILogger<HangfireRecurringTaskProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(recurringJobManager);
        ArgumentNullException.ThrowIfNull(taskOptions);

        this._recurringJobManager = recurringJobManager;
        this._logger = logger;
        this._defaultQueue = taskOptions.Value.DefaultQueue ?? global::Hangfire.States.EnqueuedState.DefaultQueue;
    }

    /// <inheritdoc />
    public Task RegisterSchedulesAsync(
        IReadOnlyList<RecurringTaskSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        var desiredIds = new HashSet<string>(schedules.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var schedule in schedules)
        {
            var scheduleId = schedule.Name;
            desiredIds.Add(scheduleId);

            var envelope = new BackgroundTaskEnvelope(schedule.BackgroundTask);
            var queue = schedule.Queue ?? this._defaultQueue;

            var job = global::Hangfire.Common.Job.FromExpression<HangfireTaskExecutor>(
                e => e.ExecuteAsync(
                    new HangfireBackgroundTaskWrapper(envelope),
                    null!,
                    CancellationToken.None),
                queue);

            this._recurringJobManager.AddOrUpdate(
                scheduleId,
                job,
                schedule.CronExpression,
                new RecurringJobOptions
                {
                    TimeZone = schedule.TimeZone,
                });

            this._logger.LogDebug(
                "Registered recurring task {TaskName} ({TaskId}) with cron {CronExpression}",
                schedule.Name,
                scheduleId,
                schedule.CronExpression);
        }

        // Clean up stale recurring jobs that are no longer declared.
        try
        {
            var existingJobs = JobStorage.Current.GetConnection().GetRecurringJobs();

            foreach (var existing in existingJobs)
            {
                if (!desiredIds.Contains(existing.Id))
                {
                    this._recurringJobManager.RemoveIfExists(existing.Id);

                    this._logger.LogInformation(
                        "Removed stale recurring task {TaskId} (no longer declared by any IRecurringTaskScheduler)",
                        existing.Id);
                }
            }
        }
        catch (Exception ex)
        {
            // Stale cleanup is best-effort — don't fail startup.
            this._logger.LogWarning(ex, "Failed to clean up stale recurring tasks");
        }

        return Task.CompletedTask;
    }
}
