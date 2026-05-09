using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Default implementation of <see cref="IClaimInspector"/> that supports wildcard matching
/// and hierarchical resource key expansion. The matching rules are:
/// <list type="bullet">
///   <item>If the demanded claim value is <c>"*"</c>, only the claim type and value type need to match.</item>
///   <item>If a user holds a claim whose value is a prefix of the demanded value, the demand is satisfied
///         (hierarchical inheritance — e.g. "Account/6" satisfies "Account/6/Team/5/User/1").</item>
///   <item>Otherwise, exact match on type, value, and value type is required.</item>
/// </list>
/// </summary>
[PublicAPI]
public sealed class ClaimInspector : IClaimInspector
{
    private readonly IEnumerable<IResourceKeyExpander> _resourceKeyExpanders;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimInspector"/> class.
    /// </summary>
    /// <param name="resourceKeyExpanders">Registered resource key expanders for hierarchical matching.</param>
    public ClaimInspector(IEnumerable<IResourceKeyExpander> resourceKeyExpanders)
    {
        this._resourceKeyExpanders = resourceKeyExpanders;
    }

    /// <inheritdoc />
    public bool IsDemandedClaimFulfilled(IClaimsHolder userClaims, Claim demandedClaim)
    {
        var matchingClaims = userClaims.GetClaimsByValueType(demandedClaim.ValueType);

        foreach (var userClaim in matchingClaims)
        {
            if (!string.Equals(userClaim.Type, demandedClaim.Type, StringComparison.Ordinal))
            {
                continue;
            }

            // Wildcard: user has a system-wide grant for this permission
            if (string.Equals(userClaim.Value, "*", StringComparison.Ordinal))
            {
                return true;
            }

            // Demanded is wildcard: just need to hold any claim of this type/valueType
            if (string.Equals(demandedClaim.Value, "*", StringComparison.Ordinal))
            {
                return true;
            }

            // Exact match
            if (string.Equals(userClaim.Value, demandedClaim.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Hierarchical prefix: user has higher-level grant that covers the demand
            if (demandedClaim.Value.StartsWith(userClaim.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Try expansion: expand the demanded resource key and re-check
        var expandedKey = this.TryExpand(demandedClaim.Value);
        if (expandedKey != null && !string.Equals(expandedKey, demandedClaim.Value, StringComparison.Ordinal))
        {
            var expandedClaim = new Claim(demandedClaim.Type, expandedKey, demandedClaim.ValueType);
            return this.IsDemandedClaimFulfilled(userClaims, expandedClaim);
        }

        return false;
    }

    private string? TryExpand(string resourceKey)
    {
        foreach (var expander in this._resourceKeyExpanders)
        {
            var expanded = expander.Expand(resourceKey);
            if (expanded != null)
            {
                return expanded;
            }
        }

        return null;
    }
}
