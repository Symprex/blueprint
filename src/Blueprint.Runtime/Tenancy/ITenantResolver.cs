using System.Security.Claims;

namespace Blueprint.Tenancy;

/// <summary>
/// Resolves the current tenant identifier from the authenticated user's claims.
/// Implement this interface to define how your application extracts tenant identity
/// from JWT tokens or other claim sources.
/// </summary>
/// <remarks>
/// <para>
/// The generated pipeline calls <see cref="ResolveTenantId"/> during the Population stage
/// and feeds the result into <see cref="ITenantAccessor.Current"/> and any <c>TenantId</c>
/// properties on the operation.
/// </para>
/// <para>
/// For Signature365, the implementation extracts the <c>Sig365TenantId</c> claim:
/// <code>
/// public string? ResolveTenantId(ClaimsPrincipal principal)
///     => principal.FindFirst("Sig365TenantId")?.Value;
/// </code>
/// </para>
/// </remarks>
public interface ITenantResolver
{
    /// <summary>
    /// Extracts the tenant identifier from the principal's claims.
    /// Returns <c>null</c> when the principal has no tenant context (e.g., anonymous, internal).
    /// </summary>
    /// <param name="principal">The authenticated user's claims principal.</param>
    /// <returns>The tenant identifier, or <c>null</c> if no tenant context is available.</returns>
    string? ResolveTenantId(ClaimsPrincipal principal);
}
