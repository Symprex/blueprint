using System.Net;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a plain-text response that bypasses JSON serialization and writes a raw text body.
/// Defaults to <c>200 OK</c> with <c>text/plain</c> content type, but both the status code
/// and content type can be overridden to support content types like <c>text/vcard</c> or
/// error responses with non-200 status codes.
/// </summary>
public class PlainTextResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlainTextResult"/> class with the given text content
    /// and a <c>200 OK</c> status code.
    /// </summary>
    /// <param name="content">The plain-text content to write to the response body.</param>
    public PlainTextResult(string content)
        : this(content, HttpStatusCode.OK)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlainTextResult"/> class with the given text content
    /// and HTTP status code.
    /// </summary>
    /// <param name="content">The plain-text content to write to the response body.</param>
    /// <param name="statusCode">The HTTP status code for the response.</param>
    public PlainTextResult(string content, HttpStatusCode statusCode)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
        this.StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the plain-text content that will be written to the response body.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets the HTTP status code for this response. Defaults to <c>200 OK</c>.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets or sets the content type header for this response. Defaults to <c>text/plain</c>.
    /// Override this to serve non-plain-text content such as <c>text/vcard</c>, <c>text/csv</c>,
    /// or <c>text/html</c>.
    /// </summary>
    public string ContentType { get; set; } = "text/plain";

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("PlainTextResult must be translated by a host-specific result executor instead of self-executing.");
    }
}
