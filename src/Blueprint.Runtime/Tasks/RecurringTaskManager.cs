using Blueprint.Tasks;

namespace Blueprint.Runtime.Tasks;

/// <summary>
/// Collects the current schedules of every registered <see cref="IRecurringTaskScheduler"/> and hands the complete set to
/// the <see cref="IRecurringTaskProvider"/>, which adds, updates and removes jobs to match it.
/// </summary>
/// <remarks>
/// <para>
/// This is legacy Blueprint's <c>RecurringTaskManager</c>. An <see cref="IPeriodicRecurringTaskProvider"/> schedules
/// <see cref="RescheduleAllAsync"/> to run periodically, as the Hangfire provider does every 30 minutes, so that a schedule
/// a scheduler starts or stops declaring while the app runs is applied, and a registration that failed is retried; for
/// such a provider it runs only then. For any other provider, <see cref="RecurringTaskRegistrationService"/> runs it once
/// when the host starts.
/// </para>
/// <para>
/// The provider always receives the complete current set, even an empty one. A scheduler that throws is logged and
/// counted as declaring nothing, so its jobs are removed along with every other schedule the set no longer contains.
/// </para>
/// </remarks>
public sealed class RecurringTaskManager
{
    private readonly IEnumerable<IRecurringTaskScheduler> _schedulers;
    private readonly IRecurringTaskProvider _provider;
    private readonly ILogger<RecurringTaskManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringTaskManager"/> class.
    /// </summary>
    /// <param name="schedulers">All registered recurring task schedulers.</param>
    /// <param name="provider">The provider that registers the schedules with the scheduling infrastructure.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RecurringTaskManager(
        IEnumerable<IRecurringTaskScheduler> schedulers,
        IRecurringTaskProvider provider,
        ILogger<RecurringTaskManager> logger)
    {
        ArgumentNullException.ThrowIfNull(schedulers);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);

        this._schedulers = schedulers;
        this._provider = provider;
        this._logger = logger;
    }

    /// <summary>
    /// Asks every scheduler for its schedules and hands the complete current set to the provider.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the registration.</param>
    /// <returns>A task that completes when the provider has registered the set.</returns>
    public async Task RescheduleAllAsync(CancellationToken cancellationToken)
    {
        var allSchedules = new List<RecurringTaskSchedule>();

        foreach (var scheduler in this._schedulers)
        {
            allSchedules.AddRange(await this.GetSchedulesAsync(scheduler).ConfigureAwait(false));
        }

        // The provider is given the current set even when it is empty, as legacy RecurringTaskManager.RescheduleAllAsync
        // always gave it: the provider removes the jobs of every schedule the set no longer contains, so skipping the call
        // would leave the app's last deleted schedule firing.
        this._logger.LogInformation(
            "Registering {Count} recurring task schedule(s) with {Provider}",
            allSchedules.Count,
            this._provider.GetType().Name);

        await this._provider.RegisterSchedulesAsync(allSchedules, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a scheduler's schedules, treating one that throws as declaring none, as legacy
    /// <c>RecurringTaskManager.GetSchedulesAsync</c> did.
    /// </summary>
    private async Task<IEnumerable<RecurringTaskSchedule>> GetSchedulesAsync(IRecurringTaskScheduler scheduler)
    {
        var schedulerName = scheduler.GetType().Name;

        try
        {
            return await scheduler.GetTaskSchedulesAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            this._logger.LogError(
                e,
                "Unhandled exception getting schedules from '{SchedulerName}'. Its recurring tasks will be removed",
                schedulerName);

            return [];
        }
    }
}
