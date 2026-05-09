using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Provides a read-only view over a user's claims for authorization inspection.
/// Implementations index claims by <see cref="Claim.ValueType"/> for efficient lookup
/// during permission evaluation.
/// </summary>
[PublicAPI]
public interface IClaimsHolder
{
    /// <summary>
    /// Returns all claims held by the user that match the given value type (permission name).
    /// </summary>
    /// <param name="valueType">The permission name to filter by (e.g. "Edit", "ViewReport").</param>
    /// <returns>An enumerable of matching claims, or empty if none match.</returns>
    IEnumerable<Claim> GetClaimsByValueType(string valueType);
}
