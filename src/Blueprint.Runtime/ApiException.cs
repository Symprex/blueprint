using System.Collections.Frozen;

using JetBrains.Annotations;

namespace Blueprint;

/// <summary>
/// An exception that carries RFC 7807 Problem Details information, enabling the
/// generated exception-translation pipeline to produce standards-based error responses.
/// </summary>
/// <remarks>
/// Use <see cref="ApiExceptionFactory"/> to consolidate well-known error types and
/// make them discoverable for OpenAPI documentation.
/// </remarks>
[PublicAPI]
public class ApiException : Exception
{
    private static readonly IReadOnlyDictionary<string, object> EmptyExtensions =
        FrozenDictionary<string, object>.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class.
    /// </summary>
    /// <param name="title">A short summary of the problem type, stable across occurrences.</param>
    /// <param name="type">A URI reference identifying the problem type.</param>
    /// <param name="detail">A human-readable explanation specific to this occurrence.</param>
    /// <param name="httpStatus">The HTTP status code for this problem.</param>
    /// <param name="extensions">Additional key-value pairs serialized with the problem response.</param>
    public ApiException(
        string title,
        string type,
        string? detail,
        int httpStatus,
        IReadOnlyDictionary<string, object>? extensions = null)
        : base(detail ?? title ?? type)
    {
        this.Title = title;
        this.Type = type;
        this.Detail = detail;
        this.HttpStatus = httpStatus;
        this.Extensions = extensions ?? EmptyExtensions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class with an inner exception.
    /// </summary>
    /// <param name="title">A short summary of the problem type, stable across occurrences.</param>
    /// <param name="type">A URI reference identifying the problem type.</param>
    /// <param name="detail">A human-readable explanation specific to this occurrence.</param>
    /// <param name="httpStatus">The HTTP status code for this problem.</param>
    /// <param name="inner">The exception that triggered this one.</param>
    /// <param name="extensions">Additional key-value pairs serialized with the problem response.</param>
    public ApiException(
        string title,
        string type,
        string? detail,
        int httpStatus,
        Exception inner,
        IReadOnlyDictionary<string, object>? extensions = null)
        : base(detail ?? title ?? type, inner)
    {
        this.Title = title;
        this.Type = type;
        this.Detail = detail;
        this.HttpStatus = httpStatus;
        this.Extensions = extensions ?? EmptyExtensions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class with a simple message.
    /// </summary>
    /// <param name="message">The error message (used as both detail and title).</param>
    /// <param name="httpStatus">The HTTP status code.</param>
    public ApiException(string message, int httpStatus = 500)
        : base(message)
    {
        this.Title = message;
        this.Detail = message;
        this.HttpStatus = httpStatus;
        this.Extensions = EmptyExtensions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class with a simple message and inner exception.
    /// </summary>
    /// <param name="message">The error message (used as both detail and title).</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="httpStatus">The HTTP status code.</param>
    public ApiException(string message, Exception innerException, int httpStatus = 500)
        : base(message, innerException)
    {
        this.Title = message;
        this.Detail = message;
        this.HttpStatus = httpStatus;
        this.Extensions = EmptyExtensions;
    }

    /// <summary>
    /// Gets the HTTP status code for this problem.
    /// </summary>
    public int HttpStatus { get; }

    /// <summary>
    /// A URI reference (RFC 3986) that identifies the problem type. When dereferenced it should
    /// provide human-readable documentation. When absent, defaults to <c>"about:blank"</c>.
    /// </summary>
    public string? Type { get; }

    /// <summary>
    /// A short, human-readable summary of the problem type. Should be stable across
    /// occurrences (not instance-specific).
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// A human-readable explanation specific to this occurrence of the problem.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Additional problem-type-specific key-value pairs. Clients can use these
    /// to make decisions when handling this error.
    /// </summary>
    public IReadOnlyDictionary<string, object> Extensions { get; }
}
