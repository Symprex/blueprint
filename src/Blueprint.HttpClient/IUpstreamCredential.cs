using System.Net.Http.Headers;

namespace Blueprint.HttpClient;

/// <summary>
/// A strategy that authenticates one outgoing request to an upstream Blueprint service, for example by setting its
/// <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// <para>
/// It is context-free, so a typed client used outside a pipeline, such as from a CLI or a background service, can use it
/// too. The transport applies it per call, last: after the caller's <see cref="CallOptions.ModifyRequest"/> and any request
/// modifiers, so what it sets wins.
/// </para>
/// <para>
/// A typed client takes one through <c>WithCredential</c>, which returns a clone; a later <c>WithCredential</c> replaces it.
/// </para>
/// </remarks>
public interface IUpstreamCredential
{
    /// <summary>
    /// Applies the credential to <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the credential is applied.</returns>
    ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>
/// A fixed bearer token, sent as <c>Authorization: Bearer {token}</c>. Signature365's <c>UpdateJwt(jwt)</c> becomes
/// <c>WithCredential(new BearerTokenCredential(jwt))</c>.
/// </summary>
public sealed class BearerTokenCredential : IUpstreamCredential
{
    private readonly string _token;

    /// <summary>
    /// Initialises a new instance of the <see cref="BearerTokenCredential"/> class.
    /// </summary>
    /// <param name="token">The bearer token, without the <c>Bearer</c> scheme.</param>
    public BearerTokenCredential(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        this._token = token;
    }

    /// <inheritdoc />
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this._token);

        return ValueTask.CompletedTask;
    }
}
