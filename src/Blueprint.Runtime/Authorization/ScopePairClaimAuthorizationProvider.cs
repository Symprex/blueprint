using System.Collections.Frozen;
using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// A production-ready <see cref="IResourceAuthorizationProvider"/> that evaluates
/// scope-pair claims from the authenticated principal. This is the authorization model
/// used by Signature 365 where JWT tokens carry scope claims in <c>{noun}::{verb}</c>
/// format (e.g. <c>"Template::Read"</c>, <c>"User::Manage"</c>).
/// </summary>
/// <remarks>
/// <para>
/// This provider uses any-of semantics: the caller is authorized if any declared
/// scope pair on the operation matches any scope claim in the principal's identity.
/// </para>
/// <para>
/// The scope claim type defaults to <c>"scope"</c> which matches
/// the standard OAuth2 convention. The separator is configurable via the constructor
/// (default <c>"::"</c> for the <c>WithCrudVerbHierarchy</c> factory,
/// matching the S365 convention of <c>Noun::Verb</c>).
/// </para>
/// <para>
/// Optionally, a set of ignored scopes can be provided to skip
/// non-API scopes such as <c>"openid"</c> or <c>"profile"</c>.
/// </para>
/// <para>
/// <b>Verb hierarchy:</b> When verb inclusions are configured via the constructor
/// (e.g. <c>"Manage" → [Create, Read, Update, Delete]</c>), a claim carrying a parent
/// verb will also satisfy requirements for any of its included child verbs. For example,
/// if a user has scope <c>"User::Manage"</c> and the operation requires
/// <c>[Resource("User", "Read")]</c>, the check succeeds because <c>Manage</c> includes
/// <c>Read</c>. This matches the S365 verb expansion model.
/// </para>
/// <para>
/// <b>Legacy scope mapping:</b> When a <see cref="LegacyScopeMapperFunc"/> is provided,
/// scope claims that do not directly match any required pair are remapped through the
/// function before a second match attempt. This supports backwards-compatible tokens
/// after scope renames (e.g. <c>"CompiledSignature::Read"</c> → <c>"MyCompiledSignature::Read"</c>).
/// </para>
/// </remarks>
/// <summary>
/// A function that maps a legacy scope claim value to zero or more replacement scope values.
/// Returning <see langword="null"/> or an empty array means no legacy mapping exists for the value.
/// </summary>
/// <param name="scopeClaimValue">The raw scope claim value that did not match any required scope pair directly.</param>
/// <returns>Replacement scope values, or <see langword="null"/> if no mapping exists.</returns>
public delegate string[]? LegacyScopeMapperFunc(string scopeClaimValue);

/// <inheritdoc cref="ScopePairClaimAuthorizationProvider"/>
public class ScopePairClaimAuthorizationProvider : IResourceAuthorizationProvider, IResolvedResourceAuthorizationProvider
{
    private static readonly Task<bool> _trueResult = Task.FromResult(true);
    private static readonly Task<bool> _falseResult = Task.FromResult(false);

    private readonly string _scopeClaimType;
    private readonly string _separator;
    private readonly FrozenSet<string> _ignoredScopes;
    private readonly LegacyScopeMapperFunc? _legacyScopeMapper;

    // Inverse mapping: required verb → list of parent verbs that also satisfy it.
    // e.g. "Create" → ["Manage"] means a "Manage" claim also satisfies a "Create" requirement.
    private readonly FrozenDictionary<string, string[]> _verbIncludedIn;

    /// <summary>
    /// The standard verb hierarchy where <c>Manage</c> includes all CRUD verbs.
    /// Matches the S365 convention: a scope claim of <c>User::Manage</c> also
    /// satisfies <c>User::Create</c>, <c>User::Read</c>, <c>User::Update</c>, and <c>User::Delete</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> StandardCrudVerbHierarchy =
        new Dictionary<string, string[]>
        {
            ["Manage"] = ["Create", "Read", "Update", "Delete"],
        };

    private static readonly Dictionary<string, string[]> _standardCrudVerbHierarchy =
        new()
        {
            ["Manage"] = ["Create", "Read", "Update", "Delete"],
        };

    /// <summary>
    /// Initialises a new instance of the <see cref="ScopePairClaimAuthorizationProvider"/> class
    /// with default settings: <c>"scope"</c> claim type, <c>"::"</c> separator, and the standard
    /// OAuth2 non-API scopes (<c>openid</c>, <c>profile</c>, <c>email</c>) ignored.
    /// </summary>
    public ScopePairClaimAuthorizationProvider()
        : this("scope", "::", ["openid", "profile", "email", "offline_access"])
    {
    }

    /// <summary>
    /// Creates a provider with the standard CRUD verb hierarchy where <c>Manage</c> includes
    /// <c>Create</c>, <c>Read</c>, <c>Update</c>, and <c>Delete</c>. This is the recommended
    /// configuration for S365 services and any application using the standard noun/verb authorization model.
    /// Uses the <c>"::"</c> separator matching S365's <c>Noun::Verb</c> convention.
    /// </summary>
    /// <param name="additionalIgnoredScopes">
    /// Extra scope values to ignore beyond the defaults (<c>openid</c>, <c>profile</c>, <c>email</c>,
    /// <c>offline_access</c>). For example, S365 ignores the legacy <c>Signature365.ReadWrite.All</c> scope.
    /// </param>
    /// <returns>A configured provider with CRUD verb hierarchy.</returns>
    public static ScopePairClaimAuthorizationProvider WithCrudVerbHierarchy(
        params string[] additionalIgnoredScopes)
    {
        return WithCrudVerbHierarchy(separator: "::", legacyScopeMapper: null, additionalIgnoredScopes);
    }

    /// <summary>
    /// Creates a provider with the standard CRUD verb hierarchy and a configurable separator and
    /// optional legacy scope mapper. For S365 services, the separator is <c>"::"</c> matching
    /// the <c>Noun::Verb</c> convention.
    /// </summary>
    /// <param name="separator">
    /// The separator between noun and verb in scope claim values (e.g. <c>"::"</c> for
    /// <c>"Template::Read"</c>, or <c>"."</c> for <c>"Template.Read"</c>).
    /// </param>
    /// <param name="legacyScopeMapper">
    /// Optional function that maps legacy scope claim values to replacement values.
    /// When a scope claim does not directly match any required scope pair, this function is called
    /// to see if the claim maps to one or more newer scope values that might match.
    /// </param>
    /// <param name="additionalIgnoredScopes">
    /// Extra scope values to ignore beyond the defaults (<c>openid</c>, <c>profile</c>, <c>email</c>,
    /// <c>offline_access</c>).
    /// </param>
    /// <returns>A configured provider with CRUD verb hierarchy.</returns>
    public static ScopePairClaimAuthorizationProvider WithCrudVerbHierarchy(
        string separator,
        LegacyScopeMapperFunc? legacyScopeMapper,
        params string[] additionalIgnoredScopes)
    {
        IEnumerable<string> allIgnored = ["openid", "profile", "email", "offline_access"];

        if (additionalIgnoredScopes.Length > 0)
        {
            allIgnored = allIgnored.Concat(additionalIgnoredScopes);
        }

        return new ScopePairClaimAuthorizationProvider("scope", separator, allIgnored, _standardCrudVerbHierarchy, legacyScopeMapper);
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="ScopePairClaimAuthorizationProvider"/> class.
    /// </summary>
    /// <param name="scopeClaimType">The claim type that carries scope-pair values.</param>
    /// <param name="separator">
    /// The separator between noun and verb in scope claim values (e.g. <c>"::"</c> for
    /// <c>"Template::Read"</c>).
    /// </param>
    /// <param name="ignoredScopes">
    /// Scope values to ignore during authorization (e.g. <c>"openid"</c>, <c>"profile"</c>).
    /// When non-null and non-empty, claims matching these values are skipped entirely.
    /// </param>
    /// <param name="verbInclusions">
    /// Optional verb hierarchy: maps parent verbs to the child verbs they include.
    /// For example, <c>{ "Manage": ["Create", "Read", "Update", "Delete"] }</c> means a
    /// claim of <c>"User::Manage"</c> also satisfies requirements for <c>"User::Create"</c>,
    /// <c>"User::Read"</c>, etc. This matches the S365 verb expansion model.
    /// </param>
    /// <param name="legacyScopeMapper">
    /// Optional function that maps legacy scope claim values to replacement values.
    /// When a scope claim does not directly match any required scope pair, this function is called
    /// to attempt remapping before giving up on that claim.
    /// </param>
    public ScopePairClaimAuthorizationProvider(
        string scopeClaimType,
        string separator,
        IEnumerable<string>? ignoredScopes = null,
        IDictionary<string, string[]>? verbInclusions = null,
        LegacyScopeMapperFunc? legacyScopeMapper = null)
    {
        this._scopeClaimType = scopeClaimType ?? throw new ArgumentNullException(nameof(scopeClaimType));
        this._separator = separator ?? throw new ArgumentNullException(nameof(separator));
        this._ignoredScopes = ignoredScopes is not null
            ? ignoredScopes.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
            : FrozenSet<string>.Empty;
        this._verbIncludedIn = BuildInverseVerbMap(verbInclusions);
        this._legacyScopeMapper = legacyScopeMapper;
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
            return _trueResult;
        }

        var identity = principal.Identity as ClaimsIdentity;

        if (identity is not { IsAuthenticated: true })
        {
            return _falseResult;
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

            // Check if this scope claim matches any required scope pair.
            // Uses span-based comparison to avoid per-comparison string allocation.
            if (this.MatchesAnyRequired(claim.Value, requiredScopePairs))
            {
                return _trueResult;
            }

            // Legacy scope remapping: if the claim didn't directly match, try mapped values
            if (this._legacyScopeMapper is not null)
            {
                var mappedScopes = this._legacyScopeMapper(claim.Value);

                if (mappedScopes is not null)
                {
                    foreach (var mapped in mappedScopes)
                    {
                        if (this.MatchesAnyRequired(mapped, requiredScopePairs))
                        {
                            return _trueResult;
                        }
                    }
                }
            }
        }

        // If the identity has no API scope claims at all, fall through to the
        // configurable no-scopes behavior (default: deny)
        if (!hasAnyScopeClaims)
        {
            return this.AllowWhenNoScopeClaims(operationContext, principal) ? _trueResult : _falseResult;
        }

        return _falseResult;
    }

    /// <summary>
    /// Evaluates authorization using pre-expanded <see cref="ResolvedScopePair"/> instances.
    /// The same any-of semantics as the legacy overload apply: the caller is authorized if
    /// any expanded scope pair matches any scope claim in the principal's identity.
    /// </summary>
    /// <remarks>
    /// This overload is preferred by generated pipelines when both interfaces are registered
    /// on the same instance. It avoids the template-expansion overhead entirely because the
    /// generated code already substitutes operation properties and claim values at compile time.
    /// </remarks>
    /// <param name="operationContext">The current operation context.</param>
    /// <param name="principal">The authenticated principal.</param>
    /// <param name="resolvedScopePairs">Pre-expanded scope pairs from the generated pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when at least one scope pair is satisfied.</returns>
    public Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
        CancellationToken cancellationToken)
    {
        if (resolvedScopePairs.Count == 0)
        {
            return _trueResult;
        }

        var identity = principal.Identity as ClaimsIdentity;

        if (identity is not { IsAuthenticated: true })
        {
            return _falseResult;
        }

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

            // Uses span-based comparison to avoid per-comparison string allocation.
            if (this.MatchesAnyResolved(claim.Value, resolvedScopePairs))
            {
                return _trueResult;
            }

            // Legacy scope remapping: if the claim didn't directly match, try mapped values
            if (this._legacyScopeMapper is not null)
            {
                var mappedScopes = this._legacyScopeMapper(claim.Value);

                if (mappedScopes is not null)
                {
                    foreach (var mapped in mappedScopes)
                    {
                        if (this.MatchesAnyResolved(mapped, resolvedScopePairs))
                        {
                            return _trueResult;
                        }
                    }
                }
            }
        }

        if (!hasAnyScopeClaims)
        {
            return this.AllowWhenNoScopeClaims(operationContext, principal) ? _trueResult : _falseResult;
        }

        return _falseResult;
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

    /// <summary>
    /// Checks whether a scope value (claim or mapped) matches any of the required scope pairs,
    /// including verb hierarchy expansion.
    /// </summary>
    private bool MatchesAnyRequired(string scopeValue, IReadOnlyList<ScopePair> requiredScopePairs)
    {
        foreach (var required in requiredScopePairs)
        {
            if (MatchesScopePair(scopeValue, required.Noun, this._separator, required.Verb))
            {
                return true;
            }

            // Verb hierarchy: if the scope value carries a parent verb (e.g. "Manage"),
            // check whether it includes the required verb (e.g. "Create").
            if (this._verbIncludedIn.TryGetValue(required.Verb, out var parentVerbs))
            {
                foreach (var parentVerb in parentVerbs)
                {
                    if (MatchesScopePair(scopeValue, required.Noun, this._separator, parentVerb))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a scope value (claim or mapped) matches any of the resolved scope pairs,
    /// including verb hierarchy expansion.
    /// </summary>
    private bool MatchesAnyResolved(string scopeValue, IReadOnlyList<ResolvedScopePair> resolvedScopePairs)
    {
        foreach (var required in resolvedScopePairs)
        {
            if (MatchesScopePair(scopeValue, required.Noun, this._separator, required.Verb))
            {
                return true;
            }

            if (this._verbIncludedIn.TryGetValue(required.Verb, out var parentVerbs))
            {
                foreach (var parentVerb in parentVerbs)
                {
                    if (MatchesScopePair(scopeValue, required.Noun, this._separator, parentVerb))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Zero-allocation scope-pair comparison. Checks whether <paramref name="claimValue"/> equals
    /// <c>{noun}{separator}{verb}</c> using ordinal-ignore-case span slicing, avoiding the
    /// per-comparison string interpolation that would otherwise allocate on every request.
    /// </summary>
    private static bool MatchesScopePair(
        string claimValue,
        string noun,
        string separator,
        string verb)
    {
        var expectedLength = noun.Length + separator.Length + verb.Length;

        if (claimValue.Length != expectedLength)
        {
            return false;
        }

        var span = claimValue.AsSpan();

        return span[..noun.Length].Equals(noun.AsSpan(), StringComparison.OrdinalIgnoreCase)
            && span.Slice(noun.Length, separator.Length).Equals(separator.AsSpan(), StringComparison.OrdinalIgnoreCase)
            && span[(noun.Length + separator.Length)..].Equals(verb.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the inverse verb mapping from <c>verbInclusions</c>. The input maps parent verbs
    /// to their children (e.g. <c>Manage → [Create, Read, Update, Delete]</c>). The output
    /// maps each child verb to the parent verbs that include it (e.g. <c>Create → [Manage]</c>),
    /// enabling O(1) lookup during authorization: "which parent verbs would also satisfy this
    /// required verb?"
    /// </summary>
    private static FrozenDictionary<string, string[]> BuildInverseVerbMap(
        IDictionary<string, string[]>? verbInclusions)
    {
        if (verbInclusions is null || verbInclusions.Count == 0)
        {
            return FrozenDictionary<string, string[]>.Empty;
        }

        var inverse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (parentVerb, childVerbs) in verbInclusions)
        {
            foreach (var childVerb in childVerbs)
            {
                if (!inverse.TryGetValue(childVerb, out var parents))
                {
                    parents = [];
                    inverse[childVerb] = parents;
                }

                parents.Add(parentVerb);
            }
        }

        return inverse.ToFrozenDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}
