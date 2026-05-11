using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Default implementation of <see cref="IClaimsHolder"/> that indexes claims by
/// <see cref="Claim.ValueType"/> for O(1) lookup during permission evaluation.
/// </summary>
[PublicAPI]
public sealed class ClaimsHolder : IClaimsHolder
{
    private readonly ILookup<string, Claim> _claimsByValueType;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimsHolder"/> class.
    /// </summary>
    /// <param name="claims">The full set of claims for this user.</param>
    public ClaimsHolder(IEnumerable<Claim> claims)
    {
        // Single materialization: build the array once, then wrap as read-only
        // and index by value type. Avoids the double-copy of ToList().AsReadOnly().
        var materialized = claims as Claim[] ?? claims.ToArray();
        this.Claims = materialized;
        this._claimsByValueType = materialized.ToLookup(static c => c.ValueType, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets all claims held by the user.
    /// </summary>
    public IReadOnlyList<Claim> Claims { get; }

    /// <inheritdoc />
    public IEnumerable<Claim> GetClaimsByValueType(string valueType)
    {
        return this._claimsByValueType[valueType];
    }
}
