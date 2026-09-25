using System.Net;

namespace Blueprint.HttpClient;

/// <summary>
/// Thrown by <see cref="BlueprintHttpClient{TApp}"/> when a call to a remote Blueprint API fails: the API returned a
/// non-success HTTP status code (4xx or 5xx), or could not be reached, or did not answer in time.
/// </summary>
/// <remarks>
/// <para>
/// It is an <see cref="ApiException"/> carrying the problem the failure describes, as the typed client of legacy
/// Signature365 threw: the remote API's problem details (<see cref="ApiException.Title"/>, <see cref="ApiException.Type"/>,
/// <see cref="ApiException.HttpStatus"/>, <see cref="ApiException.Detail"/> and <see cref="ApiException.Extensions"/>)
/// when it returned them, or a fixed problem for each other kind of failure. So a caller can match it the way it matches
/// an exception thrown in-process, and a Blueprint pipeline that lets it escape answers with that same problem.
/// </para>
/// <para>
/// What the remote end said about its own internals is kept out of <see cref="ApiException.Detail"/> and
/// <see cref="ApiException.Extensions"/>, and so out of any response written from this exception: the detail of a 5xx
/// response, and the message of a connection failure (which names the host), go to <see cref="Exception.InnerException"/>
/// instead, and the <c>stackTrace</c> extension of a 5xx response, or of a problem whose status is 5xx, is dropped. A
/// problem's <c>instance</c> is never carried.
/// </para>
/// <para>
/// The <see cref="StatusCode"/> and <see cref="ResponseBody"/> properties carry the raw response details for diagnostics.
/// </para>
/// </remarks>
public class BlueprintApiException : ApiException
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintApiException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code from the response.</param>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="responseBody">The raw response body, if available.</param>
    public BlueprintApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message, (int)statusCode)
    {
        this.StatusCode = statusCode;
        this.ResponseBody = responseBody;
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintApiException"/> class for a problem.
    /// </summary>
    /// <param name="problem">The problem the failure describes.</param>
    /// <param name="statusCode">The HTTP status code from the response, or the one the failure maps to.</param>
    /// <param name="responseBody">The raw response body, if there was one.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    internal BlueprintApiException(
        ApiProblem problem,
        HttpStatusCode statusCode,
        string? responseBody,
        Exception? innerException)
        : base(problem.Title!, problem.Type!, problem.Detail, problem.HttpStatus, innerException!, problem.Extensions)
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

    /// <summary>
    /// The problem a failed call describes. <see cref="Title"/> and <see cref="Type"/> are those the remote API sent,
    /// which may be absent.
    /// </summary>
    /// <param name="Title">A short summary of the problem type.</param>
    /// <param name="Type">A reference identifying the problem type.</param>
    /// <param name="Detail">An explanation specific to this occurrence.</param>
    /// <param name="HttpStatus">The HTTP status of the problem.</param>
    /// <param name="Extensions">Further members of the problem details, if any.</param>
    internal sealed record ApiProblem(
        string? Title,
        string? Type,
        string? Detail,
        int HttpStatus,
        IReadOnlyDictionary<string, object>? Extensions = null);
}
