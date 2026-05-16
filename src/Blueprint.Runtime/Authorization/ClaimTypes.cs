namespace Blueprint.Authorization;

/// <summary>
/// Standard claim type URIs used by Blueprint's authorization infrastructure.
/// </summary>
public static class ClaimTypes
{
    /// <summary>
    /// A claim type indicating the user has a permission (e.g. "View Patient Records"),
    /// typically scoped to a resource but may be global.
    /// </summary>
    public const string Permission = "urn:claims/permission";

    /// <summary>
    /// A claim type indicating the user has a role (e.g. "Manager"),
    /// typically scoped to a resource but may be global.
    /// </summary>
    public const string Role = "urn:claims/role";
}
