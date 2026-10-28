namespace Blueprint.Model;

/// <summary>
/// Resolves as much of an operation's scope pairs as is known before any resource exists, so the resource authorization
/// providers can decide whether the operation may be listed for the caller, as an authorisation manifest lists it.
/// Generated for every operation that declares scope pairs.
/// </summary>
/// <remarks>
/// <para>
/// With no resource, only the caller's own <c>{Auth.X}</c> tokens can be resolved, as an execution's generated check
/// resolves them. Each key is cut at its first property token, so a scope pair's
/// <see cref="ResolvedScopePair.ExpandedResourceKey"/> is the key's literal prefix: <c>/Tenants/ten_1/Users/</c> for
/// <c>/Tenants/{Auth.AccountId}/Users/{Id}</c>. A provider matches it prefix-compatibly.
/// </para>
/// <para>
/// A prefix says what a key could be, not what it is, so it is never valid for an execution decision. Only a link with no
/// resource is decided with it.
/// </para>
/// </remarks>
/// <param name="resolve">
/// Given a lookup of the caller's values by <c>{Auth.X}</c> name, the operation's scope pairs with each key cut to its
/// literal prefix.
/// </param>
public sealed class OperationListingAuthorizationFeatureData(
    Func<Func<string, string?>, IReadOnlyList<ResolvedScopePair>> resolve)
{
    /// <summary>
    /// Resolves the operation's scope pairs as far as the caller's own values allow.
    /// </summary>
    /// <param name="callerValue">Gives the caller's value for an <c>{Auth.X}</c> token's name, or <see langword="null"/>.</param>
    /// <returns>The scope pairs, each key cut to its literal prefix.</returns>
    public IReadOnlyList<ResolvedScopePair> Resolve(Func<string, string?> callerValue)
    {
        ArgumentNullException.ThrowIfNull(callerValue);

        return resolve(callerValue);
    }
}
