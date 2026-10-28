using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// An <see cref="IResourceGrantSource"/> keyed by scope pair, reading grants from the caller's claims of a configured type
/// whose value pairs a resource key with a scope: <c>{resourceKey};{Noun}{separator}{Verb}</c>, such as
/// <c>/Accounts/acc_1/*;Folder::Update</c>.
/// </summary>
/// <remarks>
/// <para>
/// A claim gives its key as a grant for the scope pair it names, and for no other. Noun and verb are compared
/// case-insensitively, as <see cref="ScopePairClaimAuthorizationProvider"/> compares them, and the key keeps its case,
/// because grants match ordinally. No verb implies another here: a claim for <c>Folder::Manage</c> is not a grant for
/// <c>Folder::Update</c>.
/// </para>
/// <para>
/// A value is read only when it holds exactly one <c>;</c>, names the scope pair exactly, and its key parses with
/// <see cref="ResourceGrant.TryParse"/>, so is absolute. Any other value is skipped, never thrown on, so that one malformed
/// claim refuses nothing more than itself. Only the claims of authenticated identities are read.
/// </para>
/// </remarks>
public sealed class ScopePairClaimResourceGrantSource : IResourceGrantSource
{
    private const char KeySeparator = ';';

    private readonly string _claimType;
    private readonly string _separator;

    /// <summary>
    /// Initialises a new instance of the <see cref="ScopePairClaimResourceGrantSource"/> class.
    /// </summary>
    /// <param name="claimType">The type of the claims that carry grants, compared case-insensitively.</param>
    /// <param name="separator">The separator between noun and verb in a claim's scope, such as <c>::</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="claimType"/> or <paramref name="separator"/> is null or empty.</exception>
    public ScopePairClaimResourceGrantSource(string claimType, string separator)
    {
        ArgumentException.ThrowIfNullOrEmpty(claimType);
        ArgumentException.ThrowIfNullOrEmpty(separator);

        this._claimType = claimType;
        this._separator = separator;
    }

    /// <inheritdoc />
    public ValueTask<ResourceGrants> GetGrantsAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        ApiOperationDescriptor operation,
        ResolvedScopePair scopePair,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        List<ResourceGrant>? grants = null;

        foreach (var identity in principal.Identities)
        {
            if (!identity.IsAuthenticated)
            {
                continue;
            }

            foreach (var claim in identity.Claims)
            {
                if (string.Equals(claim.Type, this._claimType, StringComparison.OrdinalIgnoreCase)
                    && this.TryReadGrant(claim.Value, scopePair, out var grant))
                {
                    (grants ??= []).Add(grant);
                }
            }
        }

        return new ValueTask<ResourceGrants>(grants is null ? ResourceGrants.Empty : new ResourceGrants(grants));
    }

    /// <summary>
    /// Reads the grant a claim value gives for <paramref name="scopePair"/>. The scope is compared before the key is cut out,
    /// so a claim for another pair allocates nothing.
    /// </summary>
    private bool TryReadGrant(string value, ResolvedScopePair scopePair, out ResourceGrant grant)
    {
        grant = default;

        var keyEnd = value.IndexOf(KeySeparator, StringComparison.Ordinal);

        if (keyEnd < 0 || value.IndexOf(KeySeparator, keyEnd + 1) >= 0)
        {
            return false;
        }

        return this.IsScopePair(value.AsSpan(keyEnd + 1), scopePair)
               && ResourceGrant.TryParse(value[..keyEnd], out grant);
    }

    /// <summary>
    /// Whether <paramref name="scope"/> is exactly <c>{Noun}{separator}{Verb}</c> for <paramref name="scopePair"/>, the noun
    /// and verb compared case-insensitively.
    /// </summary>
    private bool IsScopePair(ReadOnlySpan<char> scope, ResolvedScopePair scopePair)
    {
        var noun = scopePair.Noun.AsSpan();
        var verb = scopePair.Verb.AsSpan();

        return scope.Length == noun.Length + this._separator.Length + verb.Length
               && scope[..noun.Length].Equals(noun, StringComparison.OrdinalIgnoreCase)
               && scope.Slice(noun.Length, this._separator.Length).Equals(this._separator, StringComparison.Ordinal)
               && scope[(noun.Length + this._separator.Length)..].Equals(verb, StringComparison.OrdinalIgnoreCase);
    }
}
