namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a semantic conflict outcome that keeps the body-bearing `409 Conflict` path distinct from success and
/// created/accepted resource flows.
/// </summary>
public class ConflictResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictResult"/> class with the conflict payload.
    /// </summary>
    /// <param name="content">The conflict payload that hosts may serialize in a `409 Conflict` response body.</param>
    public ConflictResult(object content)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
    }

    /// <summary>
    /// Gets the conflict payload that host executors can serialize when materializing the semantic result.
    /// </summary>
    public object Content { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("ConflictResult must be translated by a host-specific result executor instead of self-executing.");
    }

    /// <inheritdoc />
    public override T Unwrap<T>()
    {
        return this.Content is T result
            ? result
            : throw new InvalidCastException("Cannot cast conflict result content of type " + this.Content.GetType().Name + " to " + typeof(T).Name);
    }
}

/// <summary>
/// Represents a semantic conflict outcome with a typed body.
/// </summary>
/// <typeparam name="T">The type of conflict payload that will be serialized by the host.</typeparam>
public sealed class ConflictResult<T> : ConflictResult where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictResult{T}"/> class.
    /// </summary>
    /// <param name="content">The conflict payload.</param>
    public ConflictResult(T content)
        : base(content)
    {
    }

    /// <summary>
    /// Gets the conflict payload with its original static type.
    /// </summary>
    public new T Content => (T)base.Content;
}
