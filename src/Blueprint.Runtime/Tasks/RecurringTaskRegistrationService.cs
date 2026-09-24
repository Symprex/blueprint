using Blueprint.Tasks;

namespace Blueprint.Runtime.Tasks;

/// <summary>
/// A hosted service that discovers all <see cref="IRecurringTaskScheduler"/> implementations
/// at startup, collects their schedules, and pushes them to the registered
/// <see cref="IRecurringTaskProvider"/> for infrastructure-level registration (e.g., Hangfire recurring jobs).
/// </summary>
/// <remarks>
/// <para>
/// This is the compile-time equivalent of legacy Blueprint's <c>RecurringTaskSetupTask</c> that ran during
/// app startup. It ensures all declared recurring tasks are registered (or updated/removed) when the
/// application starts.
/// </para>
/// <para>
/// The provider always receives the complete current set, even an empty one, and a scheduler that throws declares
/// nothing, as in legacy's <c>RecurringTaskManager</c>. The provider removes whatever that set no longer contains, so
/// register this service (through <c>AddRecurringTaskProvider</c>) only in the process that registers every one of the
/// app's recurring task schedulers. Signature365 did that under its task-processor condition, <c>ShouldRunTaskProcessor</c>.
/// </para>
/// </remarks>
public sealed class RecurringTaskRegistrationService : IHostedService
{
    private readonly IEnumerable<IRecurringTaskScheduler> _schedulers;
    private readonly IRecurringTaskProvider? _provider;
    private readonly ILogger<RecurringTaskRegistrationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringTaskRegistrationService"/> class.
    /// </summary>
    /// <param name="schedulers">All registered recurring task schedulers.</param>
    /// <param name="provider">The recurring task provider (may be null if none is registered).</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RecurringTaskRegistrationService(
        IEnumerable<IRecurringTaskScheduler> schedulers,
        ILogger<RecurringTaskRegistrationService> logger,
        IRecurringTaskProvider? provider = null)
    {
        this._schedulers = schedulers;
        this._logger = logger;
        this._provider = provider;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this._provider is null)
        {
            this._logger.LogDebug(
                "No IRecurringTaskProvider registered — skipping recurring task registration");
            return;
        }

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

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets a scheduler's schedules, treating one that throws as declaring none, as legacy
    /// <c>RecurringTaskManager.GetSchedulesAsync</c> did: the failure is logged, and the provider then removes that
    /// scheduler's jobs along with every other schedule the current set no longer contains.
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
