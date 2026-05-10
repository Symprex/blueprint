using System.ComponentModel;
using Blueprint.Runtime.Tasks;
using Hangfire.Server;

namespace Blueprint.Tasks.Hangfire;

/// <summary>
/// The Hangfire entry point for executing a background task. Hangfire deserializes the
/// <see cref="HangfireBackgroundTaskWrapper"/> and calls <see cref="ExecuteAsync"/>, which
/// delegates to Blueprint's <see cref="TaskExecutor"/> for pipeline execution.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[DisplayName("{0}")]</c> attribute causes Hangfire to use the wrapper's
/// <see cref="HangfireBackgroundTaskWrapper.ToString"/> for the dashboard job name.
/// </para>
/// </remarks>
public sealed class HangfireTaskExecutor
{
    private readonly TaskExecutor _taskExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="HangfireTaskExecutor"/> class.
    /// </summary>
    /// <param name="taskExecutor">The Blueprint task executor that runs the generated pipeline.</param>
    public HangfireTaskExecutor(TaskExecutor taskExecutor)
    {
        this._taskExecutor = taskExecutor ?? throw new ArgumentNullException(nameof(taskExecutor));
    }

    /// <summary>
    /// Executes the wrapped background task through Blueprint's generated pipeline.
    /// </summary>
    /// <param name="task">The Hangfire-serialized task wrapper containing the envelope.</param>
    /// <param name="context">The Hangfire <see cref="PerformContext"/> providing job metadata.</param>
    /// <param name="cancellationToken">Token to abort execution (Hangfire shutdown / delete).</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous execution.</returns>
    [DisplayName("{0}")]
    public async Task ExecuteAsync(
        HangfireBackgroundTaskWrapper task,
        PerformContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        var attempt = context?.GetJobParameter<int?>("RetryCount");

        await this._taskExecutor.ExecuteAsync(
            task.Envelope,
            activity =>
            {
                activity.SetTag("messaging.system", "hangfire");
                activity.SetTag("messaging.message_id", context?.BackgroundJob?.Id);
                activity.SetTag("hangfire.retryAttempt", (attempt ?? 0).ToString());
            },
            cancellationToken);
    }
}
