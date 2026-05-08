using JetBrains.Annotations;

namespace Blueprint.Runtime.Authorization;

/// <summary>
/// Well-known claim type URNs used by the Blueprint authorization pipeline.
/// Consumer code can reference these constants when constructing or inspecting
/// <see cref="System.Security.Claims.Claim"/> instances that represent
/// permissions and roles within a Blueprint application.
/// </summary>
[PublicAPI]
public static class ClaimTypes
{
    /// <summary>
    /// A claim type indicating a user has a specific permission (for example
    /// "View Patient Records"), typically scoped to a resource but may be
    /// system-wide.
    /// </summary>
    public const string Permission = "urn:claims/permission";

    /// <summary>
    /// A claim type indicating a user holds a specific role (for example
    /// "Manager"), typically scoped to a resource but may be system-wide.
    /// </summary>
    public const string Role = "urn:claims/role";
}
