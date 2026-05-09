using Microsoft.Extensions.Primitives;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a 304 Not Modified HTTP response. This result carries an ETag value and optional
/// headers but no body content. It signals the client that the cached representation is still
/// valid and the full payload does not need to be re-transmitted.
/// </summary>
/// <typeparam name="T">
/// The type of content that <em>would have been</em> returned had the resource changed. This allows
/// the result to remain type-compatible with <see cref="OkResult{T}"/> in handler return types.
/// </typeparam>
public sealed class NotModifiedResult<T> : OkResult<T> where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotModifiedResult{T}"/> class.
    /// </summary>
    /// <param name="eTag">The ETag value confirming the cached representation.</param>
    /// <param name="headers">Optional additional headers to include on the response.</param>
    internal NotModifiedResult(string eTag, Dictionary<string, StringValues>? headers = null)
    {
        this.ETag = eTag;
        this.Headers["ETag"] = eTag;

        if (headers != null)
        {
            foreach (var kvp in headers)
            {
                this.Headers[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Gets the ETag that matched the client's <c>If-None-Match</c> request header.
    /// </summary>
    public string ETag { get; }

    /// <summary>
    /// Gets a value indicating this is a 304 Not Modified response (no body content).
    /// </summary>
    public bool IsNotModified => true;
}
