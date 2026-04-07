namespace Blueprint.Runtime.Exec;

/// <summary>
/// Indicates the request was rejected because one or more input bindings failed
/// (malformed JSON body, invalid query/route/header/cookie value, etc.).
/// The HTTP host translates this to a 400 Bad Request response.
/// </summary>
public sealed class BindingFailureResult : OperationResult
{
    /// <summary>
    /// Initialises a new instance of <see cref="BindingFailureResult"/> with a human-readable reason.
    /// </summary>
    /// <param name="reason">A short description of the binding failure (e.g. "Invalid JSON body", "Invalid query parameter 'pageSize'").</param>
    public BindingFailureResult(string reason)
    {
        this.Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    /// <summary>
    /// A short, human-readable description of the binding failure.
    /// </summary>
    public string Reason { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}
