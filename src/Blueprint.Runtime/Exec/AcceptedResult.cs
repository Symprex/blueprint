namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a semantic "accepted but not completed" success outcome that keeps the asynchronous `202 Accepted` path
/// distinct from immediate success or created-resource responses.
/// </summary>
public class AcceptedResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AcceptedResult"/> class with the accepted payload.
    /// </summary>
    /// <param name="content">The accepted payload that hosts may serialize in a `202 Accepted` response body.</param>
    public AcceptedResult(object content)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
    }

    /// <summary>
    /// Gets the accepted payload that host executors can serialize when materializing the semantic result.
    /// </summary>
    public object Content { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("AcceptedResult must be translated by a host-specific result executor instead of self-executing.");
    }

    /// <inheritdoc />
    public override T Unwrap<T>()
    {
        return this.Content is T result
            ? result
            : throw new InvalidCastException("Cannot cast accepted result content of type " + this.Content.GetType().Name + " to " + typeof(T).Name);
    }
}

/// <summary>
/// Represents a semantic "accepted but not completed" success outcome with a typed body.
/// </summary>
/// <typeparam name="T">The type of accepted payload that will be serialized by the host.</typeparam>
public sealed class AcceptedResult<T> : AcceptedResult where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AcceptedResult{T}"/> class.
    /// </summary>
    /// <param name="content">The accepted payload.</param>
    public AcceptedResult(T content)
        : base(content)
    {
    }

    /// <summary>
    /// Gets the accepted payload with its original static type.
    /// </summary>
    public new T Content => (T)base.Content;
}
