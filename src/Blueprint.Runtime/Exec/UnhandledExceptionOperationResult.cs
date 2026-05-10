using Blueprint.Runtime.Exec;

namespace Blueprint;

/// <summary>
/// Compatibility alias for legacy callers that still refer to the pre-canonical unhandled-exception result name.
/// </summary>
public sealed class UnhandledExceptionOperationResult : UnhandledExceptionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledExceptionOperationResult"/> class.
    /// </summary>
    /// <param name="exception">The exception.</param>
    public UnhandledExceptionOperationResult(Exception exception)
        : base(exception)
    {
    }
}
