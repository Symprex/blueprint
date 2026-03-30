namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a semantic missing-resource outcome that keeps the body-bearing `404 Not Found` path distinct from
/// success, conflict, and created/accepted resource flows.
/// </summary>
public class NotFoundResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundResult"/> class with the missing-resource payload.
    /// </summary>
    /// <param name="content">The missing-resource payload that hosts may serialize in a `404 Not Found` response body.</param>
    public NotFoundResult(object content)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
    }

    /// <summary>
    /// Gets the missing-resource payload that host executors can serialize when materializing the semantic result.
    /// </summary>
    public object Content { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("NotFoundResult must be translated by a host-specific result executor instead of self-executing.");
    }

    /// <inheritdoc />
    public override T Unwrap<T>()
    {
        return this.Content is T result
            ? result
            : throw new InvalidCastException("Cannot cast not-found result content of type " + this.Content.GetType().Name + " to " + typeof(T).Name);
    }
}

/// <summary>
/// Represents a semantic missing-resource outcome with a typed body.
/// </summary>
/// <typeparam name="T">The type of missing-resource payload that will be serialized by the host.</typeparam>
public sealed class NotFoundResult<T> : NotFoundResult where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundResult{T}"/> class.
    /// </summary>
    /// <param name="content">The missing-resource payload.</param>
    public NotFoundResult(T content)
        : base(content)
    {
    }

    /// <summary>
    /// Gets the missing-resource payload with its original static type.
    /// </summary>
    public new T Content => (T)base.Content;
}
