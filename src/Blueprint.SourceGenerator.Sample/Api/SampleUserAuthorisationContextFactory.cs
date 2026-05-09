using System.Security.Claims;
using Blueprint.Authorization;
using BlueprintClaimTypes = Blueprint.Runtime.Authorization.ClaimTypes;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates how a consumer converts ASP.NET Core's <see cref="ClaimsIdentity"/> into an
/// application-specific <see cref="IUserAuthorisationContext"/> that also implements
/// <see cref="IClaimsHolder"/> for compile-time claims authorization checks.
/// </summary>
/// <remarks>
/// In a real Signature365-style consumer, this would query the user/account store or decode
/// a JWT to build the full user context. This sample extracts standard claims and maps
/// permission claims (with type <c>urn:claims/permission</c>) into the Blueprint claims model.
/// </remarks>
public sealed class SampleUserAuthorisationContextFactory(ILogger<SampleUserAuthorisationContextFactory> logger) : IUserAuthorisationContextFactory
{
    /// <inheritdoc />
    public Task<IUserAuthorisationContext?> CreateContextAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
    {
        var userId = identity.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? identity.FindFirst("sub")?.Value;

        if (userId is null)
        {
            logger.LogWarning("Authenticated identity has no user ID claim — returning null context");
            return Task.FromResult<IUserAuthorisationContext?>(null);
        }

        var accountId = identity.FindFirst("account_id")?.Value ?? "default-account";
        var email = identity.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? $"{userId}@example.com";
        var name = identity.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? identity.Name ?? "Unknown";

        // Map ASP.NET Core permission claims to Blueprint's Claim model for IClaimsHolder.
        // JWT claims with type "urn:claims/permission" carry "{ValueType}:{ResourceKey}" values.
        // For wildcard permissions (e.g. [Permission("ManageTemplates")]) the value is just the name.
        var permissionClaims = identity.Claims
            .Where(static c => c.Type == BlueprintClaimTypes.Permission)
            .Select(static c =>
            {
                var parts = c.Value.Split(':', 2);
                return parts.Length == 2
                    ? new Authorization.Claim(BlueprintClaimTypes.Permission, parts[1], parts[0])
                    : new Authorization.Claim(BlueprintClaimTypes.Permission, "*", c.Value);
            })
            .ToList();

        var context = new SampleUserContext(userId, accountId, email, name, permissionClaims);
        logger.LogDebug("Created user context: {UserId} / {AccountId} with {ClaimCount} permission claims", userId, accountId, permissionClaims.Count);

        return Task.FromResult<IUserAuthorisationContext?>(context);
    }
}

/// <summary>
/// A minimal <see cref="IUserAuthorisationContext"/> and <see cref="IClaimsHolder"/> implementation
/// that carries the user's identity, account, and permission claims extracted from JWT claims.
/// Implementing <see cref="IClaimsHolder"/> enables the source-generated inline claims checks
/// produced by <c>ClaimsAuthorizationModule</c> for <c>[Permission]</c> and <c>[PermissionFor]</c>.
/// </summary>
public sealed class SampleUserContext(string id, string accountId, string email, string name, IReadOnlyList<Authorization.Claim> claims) : IUserAuthorisationContext, IClaimsHolder
{
    private readonly ILookup<string, Authorization.Claim> _claimsByValueType =
        claims.ToLookup(static c => c.ValueType, StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsActive => true;

    /// <inheritdoc />
    public bool IsAnonymous => false;

    /// <inheritdoc />
    public string Id => id;

    /// <inheritdoc />
    public string IdentityId => id;

    /// <inheritdoc />
    public string AccountId => accountId;

    /// <inheritdoc />
    public string Email => email;

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public IEnumerable<Authorization.Claim> GetClaimsByValueType(string valueType)
    {
        return this._claimsByValueType[valueType];
    }

    /// <inheritdoc />
    public void PopulateMetadata(Action<string, object> add)
    {
        add("enduser.id", this.Id);
        add("enduser.account_id", this.AccountId);
    }
}
