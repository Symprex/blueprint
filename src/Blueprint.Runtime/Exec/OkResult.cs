using Microsoft.Extensions.Primitives;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Marker interface for results that represent a 304 Not Modified response. Checked by the HTTP
/// executor to emit the correct status code and suppress body writing.
/// </summary>
public interface INotModifiedResult
{
    /// <summary>
    /// Gets the ETag value that matched the client's <c>If-None-Match</c> header.
    /// </summary>
    string ETag { get; }
}

/// <summary>
/// Represents a known result, a well-handled result of executing an operation that will use conneg to
/// determine _how_ to output a value and also set the status code of the HTTP response.
/// </summary>
public class OkResult : OperationResult
{
    private Dictionary<string, StringValues>? _headers;

    /// <summary>
    /// Initializes a new instance of the <see cref="OkResult" /> class with the given content.
    /// </summary>
    /// <param name="content">The content to write to the body of the output.</param>
    public OkResult(object content)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OkResult" /> class without content,
    /// for derived types that represent content-less responses (e.g. 304 Not Modified).
    /// </summary>
    protected OkResult()
    {
        this.Content = null!;
    }

    /// <summary>
    /// Gets or sets the content that will be output when this result is executed.
    /// </summary>
    public object Content { get; set; }

    /// <summary>
    /// Gets a dictionary of additional HTTP response headers to include when this result is written.
    /// The dictionary is lazily created on first access.
    /// </summary>
    public Dictionary<string, StringValues> Headers => this._headers ??= new Dictionary<string, StringValues>();

    /// <summary>
    /// Gets a value indicating whether any custom headers have been set on this result,
    /// without triggering lazy allocation.
    /// </summary>
    public bool HasHeaders => this._headers is { Count: > 0 };

    /// <summary>
    /// Creates an <see cref="OkResult{T}"/> wrapping the given content with optional response headers.
    /// </summary>
    /// <typeparam name="T">The content type.</typeparam>
    /// <param name="content">The response content.</param>
    /// <param name="headers">Optional HTTP response headers to include.</param>
    /// <returns>A new <see cref="OkResult{T}"/> instance.</returns>
    public static OkResult<T> For<T>(T content, Dictionary<string, StringValues>? headers = null) where T : notnull
    {
        var result = new OkResult<T>(content);

        if (headers != null)
        {
            foreach (var kvp in headers)
            {
                result.Headers[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

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
    /// Initializes a new instance for content-less derived types (e.g. 304 Not Modified).
    /// </summary>
    protected OkResult()
    {
    }

    /// <summary>
    /// Gets or sets the content that will be output when this result is executed.
    /// </summary>
    public new T Content => (T)base.Content;

    /// <summary>
    /// Creates a 304 Not Modified result with an ETag header but no body content. The response
    /// signals the client that its cached representation is still current.
    /// </summary>
    /// <param name="eTag">The ETag value confirming the cached representation.</param>
    /// <param name="headers">Optional additional headers (e.g. <c>Cache-Control</c>).</param>
    /// <returns>A <see cref="NotModifiedResult{T}"/> that writes a 304 status code.</returns>
    public static NotModifiedResult<T> NotModified(string eTag, Dictionary<string, StringValues>? headers = null)
    {
        return new NotModifiedResult<T>(eTag, headers);
    }
}
