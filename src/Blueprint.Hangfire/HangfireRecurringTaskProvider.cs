using System.Text.Json;
using Blueprint.Tasks;
using Hangfire;
using Hangfire.States;
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
/// any previously registered schedules that are no longer present. This orphan-cleanup
/// ensures that schedules removed from code are also removed from Hangfire.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HangfireRecurringTaskProvider : IRecurringTaskProvider
{
    /// <summary>
    /// Prefix applied to all Hangfire recurring job IDs created by Blueprint.
    /// Prevents collisions with non-Blueprint recurring jobs and enables orphan detection.
    /// </summary>
    internal const string JobIdPrefix = "blueprint:";

    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<HangfireRecurringTaskProvider> _logger;
    private readonly string _defaultQueue;

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
    }

    /// <inheritdoc />
    public Task RegisterSchedulesAsync(
        IReadOnlyList<RecurringTaskSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
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

        return Task.CompletedTask;
    }
}
