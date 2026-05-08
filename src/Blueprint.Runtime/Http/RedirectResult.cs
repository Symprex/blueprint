using Blueprint.Runtime.Exec;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Represents an HTTP redirect response. By default emits a 302 Found status, but can be configured
/// for permanent (301), see-other (303), or temporary redirect (307) via <see cref="StatusCode"/>.
/// </summary>
public class RedirectResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RedirectResult"/> class that redirects to the given URL.
    /// </summary>
    /// <param name="url">The target URL to redirect to.</param>
    public RedirectResult(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        this.Url = url;
    }

    /// <summary>
    /// Gets the target URL for the redirect.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Gets or sets the HTTP status code for the redirect. Defaults to 302 (Found).
    /// Common values: 301 (Moved Permanently), 302 (Found), 303 (See Other), 307 (Temporary Redirect).
    /// </summary>
    public int StatusCode { get; set; } = 302;

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("RedirectResult must be translated by an HTTP result executor instead of self-executing.");
    }
}
