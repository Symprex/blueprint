using System.Net.Http.Headers;

namespace Blueprint.HttpClient;

/// <summary>
/// An <see cref="IUpstreamCredential"/> that sends a fixed token as an <c>Authorization: Bearer</c> header.
/// </summary>
public sealed class BearerTokenCredential : IUpstreamCredential
{
    private readonly string _token;

    /// <summary>
    /// Initialises a new instance of the <see cref="BearerTokenCredential"/> class.
    /// </summary>
    /// <param name="token">The bearer token.</param>
    public BearerTokenCredential(string token)
    {
        this._token = token ?? throw new ArgumentNullException(nameof(token));
    }

    /// <inheritdoc />
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this._token);

        return ValueTask.CompletedTask;
    }
}
