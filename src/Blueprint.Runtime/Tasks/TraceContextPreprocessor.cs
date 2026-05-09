using System.Diagnostics;
using Blueprint.Tasks;

namespace Blueprint.Runtime.Tasks;

/// <summary>
/// Default <see cref="IBackgroundTaskPreprocessor"/> that injects W3C Trace Context headers
/// (<c>traceparent</c> and <c>tracestate</c>) into the task envelope so that background task
/// execution can be correlated with the originating request span.
/// </summary>
public sealed class TraceContextPreprocessor : IBackgroundTaskPreprocessor
{
    /// <inheritdoc />
    public void Preprocess(BackgroundTaskEnvelope envelope)
    {
        var activity = Activity.Current;

        if (activity is null)
        {
            return;
        }

        envelope.Headers ??= new Dictionary<string, string>();
        envelope.Headers["traceparent"] = activity.Id!;

        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            envelope.Headers["tracestate"] = activity.TraceStateString;
        }
    }
}
