using System.Collections.Frozen;
using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// A production-ready <see cref="IResourceAuthorizationProvider"/> that evaluates
/// scope-pair claims from the authenticated principal. This is the authorization model
/// used by Signature 365 where JWT tokens carry scope claims in <c>{noun}.{verb}</c>
/// format (e.g. <c>"Template.Read"</c>, <c>"User.Manage"</c>).
/// </summary>
/// <remarks>
/// <para>
/// This provider uses any-of semantics: the caller is authorized if any declared
/// scope pair on the operation matches any scope claim in the principal's identity.
/// </para>
/// <para>
/// The scope claim type defaults to <c>"scope"</c> which matches
/// the standard OAuth2 convention. The separator defaults to
/// <c>"."</c> which matches the S365 convention of <c>Noun.Verb</c>.
/// </para>
/// <para>
/// Optionally, a set of ignored scopes can be provided to skip
/// non-API scopes such as <c>"openid"</c> or <c>"profile"</c>.
/// </para>
/// </remarks>
public class ScopePairClaimAuthorizationProvider : IResourceAuthorizationProvider
{
    private readonly string _scopeClaimType;
    private readonly string _separator;
    private readonly FrozenSet<string> _ignoredScopes;

    /// <summary>
    /// Initialises a new instance of the <see cref="ScopePairClaimAuthorizationProvider"/> class
    /// with default settings: <c>"scope"</c> claim type, <c>"."</c> separator, and the standard
    /// OAuth2 non-API scopes (<c>openid</c>, <c>profile</c>, <c>email</c>) ignored.
    /// </summary>
    public ScopePairClaimAuthorizationProvider()
        : this("scope", ".", ["openid", "profile", "email", "offline_access"])
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="ScopePairClaimAuthorizationProvider"/> class.
    /// </summary>
    /// <param name="scopeClaimType">The claim type that carries scope-pair values.</param>
    /// <param name="separator">
    /// The separator between noun and verb in scope claim values (e.g. <c>"."</c> for
    /// <c>"Template.Read"</c>).
    /// </param>
    /// <param name="ignoredScopes">
    /// Scope values to ignore during authorization (e.g. <c>"openid"</c>, <c>"profile"</c>).
    /// When non-null and non-empty, claims matching these values are skipped entirely.
    /// </param>
    public ScopePairClaimAuthorizationProvider(
        string scopeClaimType,
        string separator,
        IEnumerable<string>? ignoredScopes = null)
    {
        this._scopeClaimType = scopeClaimType ?? throw new ArgumentNullException(nameof(scopeClaimType));
        this._separator = separator ?? throw new ArgumentNullException(nameof(separator));
        this._ignoredScopes = ignoredScopes is not null
            ? ignoredScopes.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
            : FrozenSet<string>.Empty;
    }

    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ScopePair> requiredScopePairs,
        CancellationToken cancellationToken)
    {
        if (requiredScopePairs.Count == 0)
        {
            return Task.FromResult(true);
        }

        var identity = principal.Identity as ClaimsIdentity;

        if (identity is not { IsAuthenticated: true })
        {
            return Task.FromResult(false);
        }

        // Collect non-ignored scope claims
        var hasAnyScopeClaims = false;

        foreach (var claim in identity.Claims)
        {
            if (!string.Equals(claim.Type, this._scopeClaimType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (this._ignoredScopes.Count > 0 && this._ignoredScopes.Contains(claim.Value))
            {
                continue;
            }

            hasAnyScopeClaims = true;

            // Check if this scope claim matches any required scope pair
            foreach (var required in requiredScopePairs)
            {
                var expectedScope = string.Concat(required.Noun, this._separator, required.Verb);

                if (string.Equals(claim.Value, expectedScope, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(true);
                }
            }
        }

        // If the identity has no API scope claims at all, fall through to the
        // configurable no-scopes behavior (default: deny)
        if (!hasAnyScopeClaims)
        {
            return Task.FromResult(this.AllowWhenNoScopeClaims(operationContext, principal));
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Called when the authenticated identity has no API scope claims (after filtering ignored
    /// scopes). Override to allow access for identities without scopes — the default is
    /// <see langword="false"/> (deny).
    /// </summary>
    /// <param name="operationContext">The current operation context.</param>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>
    /// <see langword="true"/> to allow access when no scope claims are present;
    /// <see langword="false"/> to deny.
    /// </returns>
    protected virtual bool AllowWhenNoScopeClaims(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal)
    {
        return false;
    }
}
