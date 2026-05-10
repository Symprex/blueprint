namespace Blueprint.Testing;

/// <summary>
/// Thrown when a test operation execution produces an unexpected result type,
/// providing diagnostic information about what actually happened in the pipeline.
/// </summary>
public sealed class BlueprintTestExecutionException : Exception
{
    /// <summary>
    /// Gets the actual <see cref="OperationResult"/> that was produced.
    /// </summary>
    public OperationResult ActualResult { get; }

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintTestExecutionException"/> class.
    /// </summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="actualResult">The result that was actually produced.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public BlueprintTestExecutionException(
        string message,
        OperationResult actualResult,
        Exception? innerException = null)
        : base(message, innerException)
    {
        this.ActualResult = actualResult;
    }
}
