using System.Net;

namespace Blueprint.HttpClient;

/// <summary>
/// Thrown when a remote Blueprint API returns a non-success HTTP status code (4xx or 5xx).
/// The <see cref="StatusCode"/> and <see cref="ResponseBody"/> properties carry the raw
/// response details for diagnostics and error handling.
/// </summary>
public class BlueprintApiException : Exception
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintApiException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code from the response.</param>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="responseBody">The raw response body, if available.</param>
    public BlueprintApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        this.StatusCode = statusCode;
        this.ResponseBody = responseBody;
    }

    /// <summary>
    /// Gets the HTTP status code returned by the remote API.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the raw response body from the remote API, if available.
    /// Useful for diagnostics when the API returns a non-standard error format.
    /// </summary>
    public string? ResponseBody { get; }
}
