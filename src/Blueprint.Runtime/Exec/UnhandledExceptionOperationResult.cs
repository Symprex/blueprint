using Blueprint.Runtime.Exec;

namespace Blueprint;

/// <summary>
/// An <see cref="OperationResult"/> that represents an unhandled exception.
/// </summary>
public class UnhandledExceptionOperationResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledExceptionOperationResult"/> class.
    /// </summary>
    /// <param name="exception">The exception.</param>
    public UnhandledExceptionOperationResult(Exception exception)
    {
        this.Exception = exception;
    }

    /// <summary>
    /// Gets the exception.
    /// </summary>
    public Exception Exception { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        // In a real implementation, this would write error details to the response
        // For now, just complete
        return Task.CompletedTask;
    }
}
