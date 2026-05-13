using Blueprint.Runtime.Exec;

namespace Blueprint.Testing;

/// <summary>
/// Thrown when a test operation execution produces an unexpected result type,
/// providing diagnostic information about what actually happened in the pipeline.
/// <para>
/// <strong>S365 migration note</strong>: this exception replaces both the legacy
/// <c>ValidationException</c> throw (when <see cref="IsValidationFailure"/> is <c>true</c>)
/// and the legacy <c>ApiException</c> rethrow (note: <see cref="ApiException"/> is rethrown
/// directly from <c>ExecuteAsync&lt;T&gt;</c>, so <c>ThrowApiExceptionAsync</c> patterns
/// continue to work). Use the convenience properties below when writing assertion shims.
/// </para>
/// </summary>
public sealed class BlueprintTestExecutionException : Exception
{
    /// <summary>
    /// Gets the actual <see cref="OperationResult"/> that was produced.
    /// </summary>
    public OperationResult ActualResult { get; }

    /// <summary>
    /// Gets <c>true</c> when the result is a validation failure, enabling quick guard checks
    /// in FluentAssertions <c>Where</c> clauses without casting.
    /// </summary>
    public bool IsValidationFailure => this.ActualResult is ValidationFailedResult;

    /// <summary>
    /// Gets the validation error dictionary when the result is a <see cref="ValidationFailedResult"/>,
    /// or <c>null</c> otherwise. Keys are property names (empty string for form-level errors),
    /// values are the error messages for each property.
    /// <para>
    /// This matches the shape S365 tests access via <c>ValidationResults[key].Should().Contain(text)</c>
    /// — update assertion extensions to read from this property instead.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors =>
        (this.ActualResult as ValidationFailedResult)?.Errors;

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
