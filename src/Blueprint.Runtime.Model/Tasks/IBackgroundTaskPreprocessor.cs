using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// A preprocessor that enriches a <see cref="BackgroundTaskEnvelope"/> with additional
/// metadata before it is dispatched to the <see cref="IBackgroundTaskScheduleProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Common uses include injecting distributed trace context (W3C Trace Context headers)
/// into <see cref="BackgroundTaskEnvelope.Headers"/> so that task execution can correlate
/// back to the originating request activity.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBackgroundTaskPreprocessor
{
    /// <summary>
    /// Processes the given envelope, typically adding headers for trace propagation or
    /// other ambient context that should travel with the task.
    /// </summary>
    /// <param name="envelope">The task envelope to enrich.</param>
    void Preprocess(BackgroundTaskEnvelope envelope);
}
