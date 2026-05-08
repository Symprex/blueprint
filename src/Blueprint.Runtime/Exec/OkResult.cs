namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a known result, a well-handled result of executing an operation that will use conneg to
/// determine _how_ to output a value and also set the status code of the HTTP response.
/// </summary>
public class OkResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OkResult" /> class with the given content.
    /// </summary>
    /// <param name="content">The content to write to the body of the output.</param>
    public OkResult(object content)
    {
        this.Content = content;
    }

    /// <summary>
    /// Gets or sets the content that will be output when this result is executed.
    /// </summary>
    public object Content { get; set; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("OkResult must be translated by a host-specific result executor instead of self-executing.");
    }

    /// <summary>
    /// Attempts to "unwrap" this operation result to the given type, throwing an exception if the result is not
    /// of the correct type.
    /// </summary>
    /// <typeparam name="T1">The type to unwrap to.</typeparam>
    /// <returns>The content of this result.</returns>
    /// <exception cref="InvalidCastException">If the object is not the correct type.</exception>
    public override T1 Unwrap<T1>()
    {
        return this.Content is T1 result
            ? result
            : throw new InvalidCastException("Cannot cast object of type " + this.Content.GetType().Name + " to " + typeof(T1).Name);
    }
}

/// <summary>
/// A specialisation of <see cref="OkResult" /> that has the content it expects typed to a
/// known type.
/// </summary>
/// <typeparam name="T">The type of content that will be used as the body of this result.</typeparam>
public class OkResult<T> : OkResult where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OkResult" /> class with the given content.
    /// </summary>
    /// <param name="content">The content to write to the body of the output.</param>
    public OkResult(T content)
        : base(content)
    {
    }

    /// <summary>
    /// Gets or sets the content that will be output when this result is executed.
    /// </summary>
    public new T Content => (T)base.Content;
}
