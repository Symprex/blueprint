using System.Collections.Concurrent;
using System.Security.Claims;
using Blueprint.EndToEnd.Tests.Harness;
using Blueprint.SourceGenerator.Sample.Gateway;

namespace Blueprint.EndToEnd.Tests.SampleSystem;

/// <summary>
/// The sample gateway's tenant-token exchange in the end-to-end suite, standing in for the identity service a real gateway
/// calls: it mints, from the issuer both apps trust, a token for the test caller whose <c>account_id</c> claim is the tenant, which the inner service's tenant isolation requires,
/// and records each exchange so a test can see what was asked for and what was sent.
/// </summary>
/// <param name="issuer">The issuer the inner service trusts.</param>
/// <param name="scopes">The <c>scope</c> claims each tenant token grants.</param>
public sealed class TestTenantTokenExchanger(TestJwtIssuer issuer, IReadOnlyList<string> scopes) : ITenantTokenExchanger
{
    private readonly ConcurrentDictionary<string, Exchange> _latest = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the latest exchange for <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>The caller's <c>Authorization</c> header it was given, and the tenant token it returned.</returns>
    public Exchange ExchangeFor(string tenantId)
    {
        return this._latest.TryGetValue(tenantId, out var exchange)
            ? exchange
            : throw new InvalidOperationException($"No tenant token was exchanged for '{tenantId}'.");
    }

    /// <inheritdoc />
    public ValueTask<string> ExchangeAsync(string? callerAuthorization, string tenantId, CancellationToken cancellationToken)
    {
        var token = issuer.CreateToken([new Claim("sub", "e2e-user"), new Claim("account_id", tenantId), .. scopes.Select(static s => new Claim("scope", s))]);
        this._latest[tenantId] = new Exchange(callerAuthorization, token);

        return ValueTask.FromResult(token);
    }

    /// <summary>
    /// One exchange.
    /// </summary>
    /// <param name="CallerAuthorization">The caller's <c>Authorization</c> header the gateway exchanged.</param>
    /// <param name="TenantToken">The tenant token it was exchanged for.</param>
    public sealed record Exchange(string? CallerAuthorization, string TenantToken);
}
