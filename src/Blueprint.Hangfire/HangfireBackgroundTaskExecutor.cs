using System.Runtime.ExceptionServices;
using System.Text.Json;
using Blueprint.Runtime.Exec;
using Blueprint.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.Hangfire;

/// <summary>
/// The Hangfire job executor that receives serialized <see cref="BackgroundTaskEnvelope"/>
/// payloads and routes them through the Blueprint pipeline for execution. Because
/// <see cref="IBackgroundTask"/> extends <see cref="Blueprint.Model.IOperation"/>, each task is dispatched
/// through the standard generated pipeline with a fresh DI scope.
/// </summary>
/// <remarks>
/// <para>
/// Hangfire invokes <see cref="ExecuteAsync"/> with the serialized payload string. The executor
/// deserializes the task, reconstructs the <see cref="BackgroundTaskEnvelope"/> with headers,
/// restores ambient context via <see cref="IBackgroundTaskContextProvider"/>, then dispatches
/// the task through <see cref="BlueprintAppExecutor.ExecuteWithNewScopeAsync"/>.
/// </para>
/// <para>
/// A task whose pipeline ends in an unhandled exception fails the job by rethrowing that exception, so Hangfire records
/// the failure and applies its retry policy. Any other non-successful result is logged and completes the job.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HangfireBackgroundTaskExecutor
{
    private readonly BlueprintAppExecutor _executor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HangfireBackgroundTaskExecutor> _logger;

    /// <summary>
    /// Initialises the executor with the Blueprint app executor and root service provider.
    /// </summary>
    /// <param name="executor">The Blueprint app executor for dispatching tasks.</param>
    /// <param name="serviceProvider">The root service provider for creating DI scopes.</param>
    /// <param name="logger">Logger for execution diagnostics.</param>
    public HangfireBackgroundTaskExecutor(
        BlueprintAppExecutor executor,
        IServiceProvider serviceProvider,
        ILogger<HangfireBackgroundTaskExecutor> logger)
    {
        this._executor = executor;
        this._serviceProvider = serviceProvider;
        this._logger = logger;
    }

    /// <summary>
    /// Executes a background task from its serialized payload. Called by Hangfire's job processor.
    /// </summary>
    /// <param name="serializedPayload">The JSON-serialized <see cref="HangfireTaskPayload"/>.</param>
    /// <param name="cancellationToken">Cancellation token from Hangfire.</param>
    /// <returns>A task that completes when the background task has been processed.</returns>
    [System.ComponentModel.DisplayName("{0}")]
    public async Task ExecuteAsync(string serializedPayload, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<HangfireTaskPayload>(serializedPayload)
            ?? throw new InvalidOperationException("Failed to deserialize Hangfire task payload.");

        var taskType = Type.GetType(payload.TaskTypeName)
            ?? throw new InvalidOperationException($"Cannot resolve task type '{payload.TaskTypeName}'.");

        var task = (IBackgroundTask)(JsonSerializer.Deserialize(payload.TaskJson, taskType)
            ?? throw new InvalidOperationException($"Failed to deserialize task body for type '{taskType.Name}'."));

        this._logger.LogDebug(
            "Hangfire executing background task {TaskType}",
            taskType.Name);

        // Restore ambient context (e.g. tenant, user) from envelope headers before execution
        if (payload.Headers is { Count: > 0 })
        {
            var contextProviders = this._serviceProvider.GetServices<IBackgroundTaskContextProvider>();

            foreach (var provider in contextProviders)
            {
                provider.RestoreContext(payload.Headers, this._serviceProvider);
            }
        }

        var result = await this._executor.ExecuteWithNewScopeAsync(task, this._serviceProvider, cancellationToken);

        if (!result.WasSuccessful)
        {
            this._logger.LogWarning(
                "Background task {TaskType} completed with non-successful result: {ResultType}",
                taskType.Name,
                result.GetType().Name);
        }

        // Hangfire sees only whether this method returned or threw, and records, retries and eventually fails a job
        // only when it throws. The pipeline turns a handler's exception into a result rather than letting it escape,
        // so it is rethrown here, with its original stack. Any other non-successful result — a validation failure,
        // say — completes the job, as legacy Blueprint's task executor did: no retry can change its outcome.
        if (result is UnhandledExceptionResult { Exception: { } exception })
        {
            ExceptionDispatchInfo.Throw(exception);
        }
    }
}
