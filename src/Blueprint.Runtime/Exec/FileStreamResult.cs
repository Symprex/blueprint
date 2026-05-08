namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a binary file download response that streams content from a <see cref="Stream"/> with the
/// specified content type and optional file name. The HTTP executor writes the stream to the response body
/// with a 200 OK status code and sets the <c>Content-Disposition</c> header when a file name is provided.
/// </summary>
/// <remarks>
/// The caller retains ownership of the <see cref="Stream"/>. The HTTP executor will read from it but
/// will not dispose it — disposal is the responsibility of the code that created the stream (typically
/// the operation handler or a <c>using</c> block wrapping the handler call).
/// </remarks>
public class FileStreamResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileStreamResult"/> class.
    /// </summary>
    /// <param name="stream">The stream containing the file content to write to the response body.</param>
    /// <param name="contentType">The MIME content type of the file (e.g. <c>application/pdf</c>, <c>image/png</c>).</param>
    /// <param name="fileName">
    /// An optional file name used to set the <c>Content-Disposition: attachment</c> header,
    /// prompting the browser to download the file. When <c>null</c>, the content is served inline.
    /// </param>
    public FileStreamResult(Stream stream, string contentType, string? fileName = null)
    {
        this.Stream = stream;
        this.ContentType = contentType;
        this.FileName = fileName;
    }

    /// <summary>
    /// Gets the stream containing the binary content to write to the response body.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// Gets the MIME content type for the response (e.g. <c>application/pdf</c>).
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the optional file name for the <c>Content-Disposition</c> header. When set, the browser
    /// is prompted to download the file with this name.
    /// </summary>
    public string? FileName { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("FileStreamResult must be translated by a host-specific result executor instead of self-executing.");
    }
}
