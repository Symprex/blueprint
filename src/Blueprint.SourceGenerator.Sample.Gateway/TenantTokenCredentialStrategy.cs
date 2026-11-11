using Blueprint.Model.Attributes;
using Microsoft.Net.Http.Headers;

namespace Blueprint.SourceGenerator.Sample.Gateway;

/// <summary>
/// Exchanges a caller's credential for a token scoped to one tenant, as an identity service does. The gateway registers
/// one; the sample has no identity service of its own, so its deployment, or the end-to-end harness, supplies it.
/// </summary>
public interface ITenantTokenExchanger
{
    /// <summary>
    /// Exchanges the caller's credential for a token scoped to <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="callerAuthorization">The caller's <c>Authorization</c> header, or <see langword="null"/> for none.</param>
    /// <param name="tenantId">The tenant the token is for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant token, sent upstream as a bearer token.</returns>
    ValueTask<string> ExchangeAsync(string? callerAuthorization, string tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Selects <see cref="TenantTokenCredentialStrategy"/> for a gateway operation's forwards: each authenticates with a token
/// the caller's credential is exchanged for, scoped to the tenant the inner operation's <see cref="TenantIdProperty"/> holds.
/// </summary>
/// <remarks>
/// The strategy reads the property from the inner operation, after the forward has filled it, so a property a binding sets
/// rather than the caller works as well as one the caller sends.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ExchangeTenantTokenAttribute : UpstreamCredentialAttribute<TenantTokenCredentialStrategy>
{
    /// <summary>
    /// Gets or sets the name of the inner operation's property holding the tenant id, usually written with <c>nameof</c>.
    /// </summary>
    public required string TenantIdProperty { get; set; }
}

/// <summary>
/// An <see cref="IUpstreamCredentialStrategy"/> that authenticates each forward with a tenant token, which
/// <see cref="ITenantTokenExchanger"/> exchanges the caller's credential for: Signature365's partner-to-tenant exchange,
/// declared rather than written into each handler.
/// </summary>
public sealed class TenantTokenCredentialStrategy : IUpstreamCredentialStrategy
{
    /// <inheritdoc />
    public IUpstreamCredential CreateCredential(ApiOperationContext context, IOperation inner, UpstreamCredentialAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inner);

        var exchange = (ExchangeTenantTokenAttribute)attribute;
        var exchanger = context.ServiceProvider.GetService<ITenantTokenExchanger>()
            ?? throw new InvalidOperationException($"{nameof(TenantTokenCredentialStrategy)} needs an {nameof(ITenantTokenExchanger)} registered in the gateway's services.");

        // The sample reads the property by reflection; a gateway with many tenant-scoped operations would give them a
        // common interface instead.
        var tenantId = inner.GetType().GetProperty(exchange.TenantIdProperty)?.GetValue(inner) as string
            ?? throw new InvalidOperationException($"{inner.GetType().Name} has no tenant id in its string property '{exchange.TenantIdProperty}'.");

        return new TenantTokenCredential(
            exchanger,
            context.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString() is { Length: > 0 } authorization ? authorization : null,
            tenantId);
    }

    /// <summary>
    /// The credential of one forward: the exchange runs when the request is sent, so it can await.
    /// </summary>
    private sealed class TenantTokenCredential(ITenantTokenExchanger exchanger, string? callerAuthorization, string tenantId) : IUpstreamCredential
    {
        public async ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = await exchanger.ExchangeAsync(callerAuthorization, tenantId, cancellationToken).ConfigureAwait(false);

            await new BearerTokenCredential(token).ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
