namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a plain-text response that bypasses JSON serialization and writes a raw text/plain body
/// with a 200 OK status code. Use this when an operation needs to return unstructured text content
/// such as health-check strings, CSV exports, or preformatted reports.
/// </summary>
public class PlainTextResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlainTextResult"/> class with the given text content.
    /// </summary>
    /// <param name="content">The plain-text content to write to the response body.</param>
    public PlainTextResult(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.Content = content;
    }

    /// <summary>
    /// Gets the plain-text content that will be written to the response body.
    /// </summary>
    public string Content { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("PlainTextResult must be translated by a host-specific result executor instead of self-executing.");
    }
}
