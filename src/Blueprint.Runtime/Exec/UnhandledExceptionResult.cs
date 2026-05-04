namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents the internal semantic fallback used when pipeline execution reaches the shared unhandled-exception boundary.
/// This result is not intended to become part of a public declared operation contract.
/// </summary>
public class UnhandledExceptionResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnhandledExceptionResult"/> class.
    /// </summary>
    /// <param name="exception">The unhandled exception captured by the shared executor shell.</param>
    public UnhandledExceptionResult(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        this.Exception = exception;
    }

    /// <inheritdoc />
    public override bool WasSuccessful => false;

    /// <summary>
    /// Gets the unhandled exception captured by the pipeline shell.
    /// </summary>
    public Exception Exception { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}
