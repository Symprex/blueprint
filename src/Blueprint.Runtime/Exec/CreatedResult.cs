namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a semantic "resource created" success outcome that keeps the created payload distinct from the ordinary
/// `200 OK` success path.
/// </summary>
public class CreatedResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatedResult"/> class with the created payload.
    /// </summary>
    /// <param name="content">The created payload that hosts may serialize in a `201 Created` response body.</param>
    public CreatedResult(object content)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
    }

    /// <summary>
    /// Gets the created payload that host executors can serialize when materializing the semantic result.
    /// </summary>
    public object Content { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("CreatedResult must be translated by a host-specific result executor instead of self-executing.");
    }

    /// <inheritdoc />
    public override T Unwrap<T>()
    {
        return this.Content is T result
            ? result
            : throw new InvalidCastException("Cannot cast created result content of type " + this.Content.GetType().Name + " to " + typeof(T).Name);
    }
}

/// <summary>
/// Represents a semantic "resource created" success outcome with a typed body.
/// </summary>
/// <typeparam name="T">The type of created payload that will be serialized by the host.</typeparam>
public sealed class CreatedResult<T> : CreatedResult where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatedResult{T}"/> class.
    /// </summary>
    /// <param name="content">The created payload.</param>
    public CreatedResult(T content)
        : base(content)
    {
    }

    /// <summary>
    /// Gets the created payload with its original static type.
    /// </summary>
    public new T Content => (T)base.Content;
}
