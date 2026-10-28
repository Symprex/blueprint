namespace Blueprint.Authorization;

/// <summary>
/// A function that maps a legacy scope claim value to zero or more replacement scope values, for
/// <see cref="ScopePairClaimAuthorizationProvider"/> to try when a claim matches no required scope pair directly.
/// Returning <see langword="null"/> or an empty array means no legacy mapping exists for the value.
/// </summary>
/// <param name="scopeClaimValue">The raw scope claim value that did not match any required scope pair directly.</param>
/// <returns>Replacement scope values, or <see langword="null"/> if no mapping exists.</returns>
public delegate string[]? LegacyScopeMapperFunc(string scopeClaimValue);
