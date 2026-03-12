using System.Diagnostics;

namespace Blueprint.Diagnostics;

/// <summary>
/// Provides the <see cref="System.Diagnostics.ActivitySource"/> for Blueprint operations.
/// </summary>
public static class BlueprintActivitySource
{
    /// <summary>
    /// The name of the activity source.
    /// </summary>
    public const string ActivitySourceName = "Blueprint";

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.ActivitySource"/> for Blueprint operations.
    /// </summary>
    public static ActivitySource ActivitySource { get; } = new ActivitySource(ActivitySourceName);

    /// <summary>
    /// Records an exception on an activity.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="escaped">Whether the exception escaped the scope.</param>
    public static void RecordException(Activity? activity, Exception exception, bool escaped)
    {
        if (activity == null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace },
            { "exception.escaped", escaped },
        };

        activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
    }
}
