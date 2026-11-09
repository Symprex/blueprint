namespace Blueprint.HttpClient;

/// <summary>
/// A strategy that authenticates a request to an upstream Blueprint API, applied to every request a client sends after
/// the caller's own request customisation, so it wins over an <c>Authorization</c> header the caller set.
/// </summary>
/// <remarks>
/// A credential knows nothing of the operation or the caller being served: it is given the request alone.
/// </remarks>
public interface IUpstreamCredential
{
    /// <summary>
    /// Applies the credential to <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the credential has been applied.</returns>
    ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
