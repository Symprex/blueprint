using Blueprint.Runtime.Exec;
using Microsoft.Net.Http.Headers;

namespace Blueprint.HttpClient;

/// <summary>
/// The default credential for a gateway's forward: the <c>Authorization</c> header of the caller the gateway is serving, sent
/// upstream unchanged.
/// </summary>
/// <remarks>
/// The header is captured when the credential is created, from the request being served, and applied to the forwarded
/// request alone, so concurrent callers sharing one client never see each other's credentials. A caller that sent no
/// <c>Authorization</c> header gets none forwarded: the request is left as it is.
/// </remarks>
public sealed class CallerAuthorizationCredential : IUpstreamCredential
{
    private readonly string? _authorization;

    /// <summary>
    /// Initialises a new instance of the <see cref="CallerAuthorizationCredential"/> class.
    /// </summary>
    /// <param name="authorization">The caller's <c>Authorization</c> header, or <see langword="null"/> or empty for none.</param>
    public CallerAuthorizationCredential(string? authorization)
    {
        this._authorization = string.IsNullOrEmpty(authorization) ? null : authorization;
    }

    /// <summary>
    /// Creates the credential for the caller of the operation <paramref name="context"/> serves.
    /// </summary>
    /// <param name="context">The operation being served.</param>
    /// <returns>
    /// The caller's credential; one that forwards nothing when the operation is not served over HTTP or the caller sent no
    /// <c>Authorization</c> header.
    /// </returns>
    public static CallerAuthorizationCredential FromContext(ApiOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CallerAuthorizationCredential(context.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString());
    }

    /// <inheritdoc />
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (this._authorization is not null)
        {
            // Replaced, never appended: the caller's header wins over one ModifyRequest or a request modifier set.
            request.Headers.Remove(HeaderNames.Authorization);
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, this._authorization);
        }

        return ValueTask.CompletedTask;
    }
}
