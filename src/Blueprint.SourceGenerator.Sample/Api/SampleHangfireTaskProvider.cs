using Blueprint.Tasks;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IBackgroundTaskScheduleProvider"/> that demonstrates the production
/// Hangfire-style integration pattern. In a real consumer, this would use Hangfire's
/// <c>IBackgroundJobClient</c> to enqueue tasks, but here we log the dispatch to prove
/// the end-to-end flow works: task buffering → preprocessor enrichment → provider dispatch.
/// </summary>
/// <remarks>
/// <para>
/// This is the equivalent of legacy Blueprint's <c>HangfireBackgroundTaskScheduleProvider</c>
/// from Signature365. The production flow:
/// <list type="number">
/// <item>Handler code calls <c>scheduler.Enqueue(task)</c> during execution</item>
/// <item>Tasks are buffered in <c>BackgroundTaskScheduler</c> until flush</item>
/// <item><c>TraceContextPreprocessor</c> injects W3C traceparent into envelope headers</item>
/// <item>On flush, this provider receives the enriched <c>BackgroundTaskEnvelope</c></item>
/// <item>Provider dispatches to infrastructure (Hangfire, queue, etc.)</item>
/// </list>
/// </para>
/// <para>
/// Register via: <c>blueprint.AddBackgroundTaskScheduleProvider&lt;SampleHangfireTaskProvider&gt;()</c>
/// which also auto-registers the production <c>BackgroundTaskScheduler</c> and
/// <c>TraceContextPreprocessor</c>.
/// </para>
/// </remarks>
public sealed class SampleHangfireTaskProvider(ILogger<SampleHangfireTaskProvider> logger) : IBackgroundTaskScheduleProvider
{
    private int _jobCounter;

    /// <inheritdoc />
    public Task<string> EnqueueAsync(BackgroundTaskEnvelope envelope)
    {
        var jobId = $"hangfire-{Interlocked.Increment(ref this._jobCounter)}";
        var taskType = envelope.Task.GetType().Name;
        var queue = envelope.GetQueueName() ?? "default";
        var hasTraceContext = envelope.Headers?.ContainsKey("traceparent") ?? false;

        logger.LogInformation(
            "📤 Hangfire ENQUEUE: {TaskType} → JobId={JobId}, Queue={Queue}, TraceContext={HasTrace}",
            taskType,
            jobId,
            queue,
            hasTraceContext);

        return Task.FromResult(jobId);
    }

    /// <inheritdoc />
    public Task<string> ScheduleAsync(BackgroundTaskEnvelope envelope, TimeSpan delay)
    {
        var jobId = $"hangfire-scheduled-{Interlocked.Increment(ref this._jobCounter)}";
        var taskType = envelope.Task.GetType().Name;

        logger.LogInformation(
            "⏰ Hangfire SCHEDULE: {TaskType} → JobId={JobId}, Delay={Delay}",
            taskType,
            jobId,
            delay);

        return Task.FromResult(jobId);
    }

    /// <inheritdoc />
    public Task<string> EnqueueChildAsync(BackgroundTaskEnvelope envelope, string parentId, BackgroundTaskContinuationOptions options)
    {
        var jobId = $"hangfire-child-{Interlocked.Increment(ref this._jobCounter)}";
        var taskType = envelope.Task.GetType().Name;

        logger.LogInformation(
            "🔗 Hangfire CONTINUATION: {TaskType} → JobId={JobId}, ParentId={ParentId}, Options={Options}",
            taskType,
            jobId,
            parentId,
            options);

        return Task.FromResult(jobId);
    }
}
