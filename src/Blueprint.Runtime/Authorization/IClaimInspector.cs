using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Inspects whether a user's claims satisfy a demanded claim, with support for
/// wildcard matching and hierarchical resource key expansion via <see cref="IResourceKeyExpander"/>.
/// </summary>
[PublicAPI]
public interface IClaimInspector
{
    /// <summary>
    /// Determines whether the demanded claim is fulfilled by the user's claims.
    /// </summary>
    /// <param name="userClaims">The holder containing the user's granted claims.</param>
    /// <param name="demandedClaim">The claim being demanded by the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the user holds a claim that satisfies the demand;
    /// <see langword="false"/> otherwise.
    /// </returns>
    bool IsDemandedClaimFulfilled(IClaimsHolder userClaims, Claim demandedClaim);
}
