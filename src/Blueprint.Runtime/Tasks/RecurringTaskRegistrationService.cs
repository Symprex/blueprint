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
    private readonly ILogger<RecurringTaskManager> _managerLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringTaskRegistrationService"/> class.
    /// </summary>
    /// <param name="schedulers">All registered recurring task schedulers.</param>
    /// <param name="provider">The recurring task provider (may be null if none is registered).</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="managerLogger">Logger for the <see cref="RecurringTaskManager"/> run at startup.</param>
    public RecurringTaskRegistrationService(
        IEnumerable<IRecurringTaskScheduler> schedulers,
        ILogger<RecurringTaskRegistrationService> logger,
        IRecurringTaskProvider? provider = null,
        ILogger<RecurringTaskManager>? managerLogger = null)
    {
        this._schedulers = schedulers;
        this._logger = logger;
        this._provider = provider;
        this._managerLogger = managerLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RecurringTaskManager>.Instance;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this._provider is null)
        {
            this._logger.LogDebug(
                "No IRecurringTaskProvider registered — skipping recurring task registration");
            return Task.CompletedTask;
        }

        return new RecurringTaskManager(this._schedulers, this._provider, this._managerLogger)
            .RescheduleAllAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
