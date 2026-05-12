using System.Security.Claims;
using Blueprint.Tenancy;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Sample implementation of <see cref="ITenantResolver"/> that extracts the tenant
/// identifier from a <c>tenant_id</c> claim. In production (e.g., Signature365),
/// the claim type would be <c>Sig365TenantId</c>.
/// </summary>
public sealed class SampleTenantResolver : ITenantResolver
{
    /// <summary>
    /// The claim type used to extract the tenant ID from JWT tokens.
    /// </summary>
    public const string TenantIdClaimType = "tenant_id";

    /// <inheritdoc />
    public string? ResolveTenantId(ClaimsPrincipal principal)
    {
        return (principal.Identity as ClaimsIdentity)?.FindFirst(TenantIdClaimType)?.Value;
    }
}
