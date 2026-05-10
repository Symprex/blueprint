using System.Net;
using JetBrains.Annotations;

namespace Blueprint;

/// <summary>
/// A factory that consolidates the creation of <see cref="ApiException"/> instances, putting
/// stable error metadata (title, type, status) in one place so that:
/// <list type="bullet">
/// <item>All throw-sites produce consistent problem responses.</item>
/// <item>Tooling can discover well-known error shapes for OpenAPI documentation.</item>
/// </list>
/// </summary>
/// <remarks>
/// Expose instances as <c>public static readonly</c> fields on handler classes so the
/// generator can discover them and enrich the operation's failure-outcome metadata.
/// </remarks>
[PublicAPI]
public sealed class ApiExceptionFactory
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiExceptionFactory"/> class.
    /// </summary>
    /// <param name="title">A short summary of the problem type, stable across occurrences.</param>
    /// <param name="type">A URI reference identifying the problem type.</param>
    /// <param name="httpStatus">The HTTP status code for this problem type.</param>
    public ApiExceptionFactory(string title, string type, int httpStatus)
    {
        this.Title = title;
        this.Type = type;
        this.HttpStatus = httpStatus;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiExceptionFactory"/> class
    /// using a strongly-typed <see cref="HttpStatusCode"/>.
    /// </summary>
    /// <param name="title">A short summary of the problem type, stable across occurrences.</param>
    /// <param name="type">A URI reference identifying the problem type.</param>
    /// <param name="httpStatus">The HTTP status code for this problem type.</param>
    public ApiExceptionFactory(string title, string type, HttpStatusCode httpStatus)
    {
        this.Title = title;
        this.Type = type;
        this.HttpStatus = (int)httpStatus;
    }

    /// <summary>
    /// Gets the HTTP status code for this problem type.
    /// </summary>
    public int HttpStatus { get; }

    /// <summary>
    /// A URI reference identifying the problem type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// A short, human-readable summary of the problem type.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Creates a new <see cref="ApiException"/> with an instance-specific detail message.
    /// </summary>
    /// <param name="detail">The instance-specific error message.</param>
    /// <param name="extensions">Optional extension data serialized with the problem response.</param>
    /// <returns>A new <see cref="ApiException"/>.</returns>
    public ApiException Create(string detail, IReadOnlyDictionary<string, object>? extensions = null)
    {
        return new ApiException(this.Title, this.Type, detail, this.HttpStatus, extensions);
    }

    /// <summary>
    /// Creates a new <see cref="ApiException"/> with optional extension data.
    /// </summary>
    /// <param name="extensions">Optional extension data serialized with the problem response.</param>
    /// <returns>A new <see cref="ApiException"/>.</returns>
    public ApiException Create(IReadOnlyDictionary<string, object>? extensions = null)
    {
        return new ApiException(this.Title, this.Type, null, this.HttpStatus, extensions);
    }

    /// <summary>
    /// Creates a new <see cref="ApiException"/> with an inner exception and detail message.
    /// </summary>
    /// <param name="innerException">The exception that caused this failure.</param>
    /// <param name="detail">The instance-specific error message.</param>
    /// <param name="extensions">Optional extension data serialized with the problem response.</param>
    /// <returns>A new <see cref="ApiException"/>.</returns>
    public ApiException Create(Exception innerException, string detail, IReadOnlyDictionary<string, object>? extensions = null)
    {
        return new ApiException(this.Title, this.Type, detail, this.HttpStatus, innerException, extensions);
    }

    /// <summary>
    /// Creates a new <see cref="ApiException"/> with an inner exception.
    /// </summary>
    /// <param name="innerException">The exception that caused this failure.</param>
    /// <param name="extensions">Optional extension data serialized with the problem response.</param>
    /// <returns>A new <see cref="ApiException"/>.</returns>
    public ApiException Create(Exception innerException, IReadOnlyDictionary<string, object>? extensions = null)
    {
        return new ApiException(this.Title, this.Type, null, this.HttpStatus, innerException, extensions);
    }
}
