using System.Net.Http.Headers;
using Blueprint.Model;

namespace Blueprint.Testing;

/// <summary>
/// A request a <see cref="BlueprintClientStub{TClient}"/> received: the operation it was built from and what was sent on the
/// wire for it.
/// </summary>
public sealed class BlueprintClientStubRequest
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintClientStubRequest"/> class.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="method">The method.</param>
    /// <param name="requestUri">The URI.</param>
    /// <param name="headers">The headers.</param>
    /// <param name="authorization">The Authorization header received.</param>
    /// <param name="requestAuthorization">The Authorization header the request set itself.</param>
    /// <param name="body">The body.</param>
    internal BlueprintClientStubRequest(
        IOperation operation,
        HttpMethod method,
        Uri? requestUri,
        IReadOnlyDictionary<string, string[]> headers,
        AuthenticationHeaderValue? authorization,
        AuthenticationHeaderValue? requestAuthorization,
        string? body)
    {
        this.Operation = operation;
        this.Method = method;
        this.RequestUri = requestUri;
        this.Headers = headers;
        this.Authorization = authorization;
        this.RequestAuthorization = requestAuthorization;
        this.Body = body;
    }

    /// <summary>
    /// Gets the operation the request was built from: a typed client's operation, or a forward's inner operation.
    /// </summary>
    public IOperation Operation { get; }

    /// <summary>
    /// Gets the request's HTTP method.
    /// </summary>
    public HttpMethod Method { get; }

    /// <summary>
    /// Gets the request's absolute URI, its query string included.
    /// </summary>
    public Uri? RequestUri { get; }

    /// <summary>
    /// Gets the request's headers as the upstream would have received them, the client's default headers included, each
    /// keyed by name, case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Headers { get; }

    /// <summary>
    /// Gets the <c>Authorization</c> header the upstream would have received, whether the request set it or it came from
    /// the client's default headers.
    /// </summary>
    public AuthenticationHeaderValue? Authorization { get; }

    /// <summary>
    /// Gets the <c>Authorization</c> header the request set itself, through a forward's credential or
    /// <see cref="Blueprint.HttpClient.CallOptions.ModifyRequest"/>; <see langword="null"/> when only the client's default
    /// headers carried one, or none did.
    /// </summary>
    /// <remarks>
    /// Assert on this, not <see cref="Authorization"/>, to prove a forward applied its credential: an app that also copies
    /// the caller's token onto the client's default headers sends the same token either way.
    /// </remarks>
    public AuthenticationHeaderValue? RequestAuthorization { get; }

    /// <summary>
    /// Gets the request's body as text, or <see langword="null"/> when it had none, as a GET has none.
    /// </summary>
    public string? Body { get; }
}
