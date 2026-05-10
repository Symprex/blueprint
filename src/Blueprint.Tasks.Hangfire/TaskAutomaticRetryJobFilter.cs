using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;

namespace Blueprint.Tasks.Hangfire;

/// <summary>
/// A Hangfire job filter that reads per-task retry configuration from
/// <see cref="AutomaticRetryAttribute"/> on the task type, falling back to
/// <see cref="BlueprintTaskOptions.DefaultRetryCount"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hangfire's built-in <see cref="global::Hangfire.AutomaticRetryAttribute"/> can only be applied
/// directly to the method being invoked. Because all Blueprint tasks execute through a single
/// <see cref="HangfireTaskExecutor.ExecuteAsync"/> method, this filter reads the retry setting
/// from the <em>task type</em> instead.
/// </para>
/// </remarks>
public sealed class TaskAutomaticRetryJobFilter : JobFilterAttribute, IElectStateFilter, IApplyStateFilter
{
    private readonly global::Hangfire.AutomaticRetryAttribute _defaultRetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskAutomaticRetryJobFilter"/> class.
    /// </summary>
    /// <param name="defaultRetryCount">The default retry count from <see cref="BlueprintTaskOptions"/>.</param>
    public TaskAutomaticRetryJobFilter(int defaultRetryCount)
    {
        this._defaultRetry = new global::Hangfire.AutomaticRetryAttribute { Attempts = defaultRetryCount };
    }

    /// <inheritdoc />
    public void OnStateElection(ElectStateContext context)
    {
        this.WithRetryAttribute(context.BackgroundJob, a => a.OnStateElection(context));
    }

    /// <inheritdoc />
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        this.WithRetryAttribute(context.BackgroundJob, a => a.OnStateApplied(context, transaction));
    }

    /// <inheritdoc />
    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        this.WithRetryAttribute(context.BackgroundJob, a => a.OnStateUnapplied(context, transaction));
    }

    private void WithRetryAttribute(BackgroundJob backgroundJob, Action<global::Hangfire.AutomaticRetryAttribute> action)
    {
        if (backgroundJob.Job is null)
        {
            return;
        }

        // All Blueprint tasks go through HangfireTaskExecutor.ExecuteAsync(wrapper, context, token).
        // The first argument is the HangfireBackgroundTaskWrapper containing the actual task.
        if (backgroundJob.Job.Type == typeof(HangfireTaskExecutor) &&
            backgroundJob.Job.Args is { Count: > 0 } &&
            backgroundJob.Job.Args[0] is HangfireBackgroundTaskWrapper wrapper)
        {
            var taskRetryAttempts = wrapper.Envelope.GetRetryAttempts();

            if (taskRetryAttempts.HasValue)
            {
                var taskRetry = new global::Hangfire.AutomaticRetryAttribute { Attempts = taskRetryAttempts.Value };
                action(taskRetry);
                return;
            }
        }

        action(this._defaultRetry);
    }
}
