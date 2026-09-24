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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringTaskRegistrationService> _logger;
    private readonly ILogger<RecurringTaskManager> _managerLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringTaskRegistrationService"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates the scope the schedulers and the provider are resolved in.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="managerLogger">Logger for the <see cref="RecurringTaskManager"/> run at startup.</param>
    /// <remarks>
    /// The provider is resolved in a scope of its own rather than injected, as legacy's
    /// <c>RecurringJobManagerRegistrationHostedService</c> resolved it: this service is a singleton, and
    /// <c>Blueprint.Hangfire</c> registers its provider scoped, as legacy did, so injecting it would capture a scoped
    /// service in a singleton, which a host that validates scopes refuses to build.
    /// </remarks>
    public RecurringTaskRegistrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecurringTaskRegistrationService> logger,
        ILogger<RecurringTaskManager>? managerLogger = null)
    {
        this._scopeFactory = scopeFactory;
        this._logger = logger;
        this._managerLogger = managerLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RecurringTaskManager>.Instance;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = this._scopeFactory.CreateAsyncScope();

        var provider = scope.ServiceProvider.GetService<IRecurringTaskProvider>();

        if (provider is null)
        {
            this._logger.LogDebug(
                "No IRecurringTaskProvider registered — skipping recurring task registration");
            return;
        }

        var schedulers = scope.ServiceProvider.GetServices<IRecurringTaskScheduler>();

        await new RecurringTaskManager(schedulers, provider, this._managerLogger)
            .RescheduleAllAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}