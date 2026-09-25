using System.Net;
using Blueprint.Http;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a plain-text response that bypasses JSON serialization and writes a raw text body.
/// Extends <see cref="HttpResult"/> so it can be returned from handlers declared as
/// <c>ICommand&lt;HttpResult&gt;</c> (the S365 webhook pattern).
/// Defaults to <c>200 OK</c> with <c>text/plain</c> content type, but both the status code
/// and content type can be overridden to support content types like <c>text/vcard</c> or
/// error responses with non-200 status codes.
/// </summary>
public sealed class PlainTextResult : HttpResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlainTextResult"/> class with the given text content
    /// and a <c>200 OK</c> status code.
    /// </summary>
    /// <param name="content">The plain-text content to write to the response body; <see langword="null"/> writes none.</param>
    public PlainTextResult(string? content)
        : this(content, HttpStatusCode.OK)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlainTextResult"/> class with the given text content
    /// and HTTP status code.
    /// </summary>
    /// <param name="content">The plain-text content to write to the response body; <see langword="null"/> writes none.</param>
    /// <param name="statusCode">The HTTP status code for the response.</param>
    /// <remarks>
    /// No content is an empty body at the status, as legacy Blueprint wrote it: it kept the content as given and wrote it
    /// through an <c>HttpResponseStreamWriter</c>, which writes nothing for <see langword="null"/> (Signature365
    /// <c>8b40ecc961</c>, <c>Blueprint.Http/PlainTextResult.cs:33-37</c>, <c>:50-63</c>).
    /// </remarks>
    public PlainTextResult(string? content, HttpStatusCode statusCode)
        : base(statusCode)
    {
        this.Content = content ?? string.Empty;
    }

    /// <summary>
    /// Gets the plain-text content that will be written to the response body, empty when the result was made with none.
    /// </summary>
    public string Content { get; }

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
