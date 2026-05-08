using System.Security.Claims;
using Blueprint.Authorization;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates how a consumer converts ASP.NET Core's <see cref="ClaimsIdentity"/> into an
/// application-specific <see cref="IUserAuthorisationContext"/>. The generated pipeline resolves
/// this factory during the Authentication stage to populate
/// <see cref="Blueprint.Runtime.Exec.ApiOperationContext.UserAuthorisationContext"/>, making the
/// user context available to <see cref="IApiAuthoriser"/> implementations and handler code.
/// </summary>
/// <remarks>
/// In a real Signature365-style consumer, this would query the user/account store or decode
/// a JWT to build the full user context. This sample extracts standard claims.
/// </remarks>
public sealed class SampleUserAuthorisationContextFactory(ILogger<SampleUserAuthorisationContextFactory> logger) : IUserAuthorisationContextFactory
{
    /// <inheritdoc />
    public Task<IUserAuthorisationContext?> CreateContextAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
    {
        var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? identity.FindFirst("sub")?.Value;

        if (userId is null)
        {
            logger.LogWarning("Authenticated identity has no user ID claim — returning null context");
            return Task.FromResult<IUserAuthorisationContext?>(null);
        }

        var accountId = identity.FindFirst("account_id")?.Value ?? "default-account";
        var email = identity.FindFirst(ClaimTypes.Email)?.Value ?? $"{userId}@example.com";
        var name = identity.FindFirst(ClaimTypes.Name)?.Value ?? identity.Name ?? "Unknown";

        var context = new SampleUserContext(userId, accountId, email, name);
        logger.LogDebug("Created user context: {UserId} / {AccountId}", userId, accountId);

        return Task.FromResult<IUserAuthorisationContext?>(context);
    }
}

/// <summary>
/// A minimal <see cref="IUserAuthorisationContext"/> implementation that carries the user's
/// identity, account, and basic profile information extracted from JWT claims.
/// </summary>
public sealed class SampleUserContext(string id, string accountId, string email, string name) : IUserAuthorisationContext
{
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
    public void PopulateMetadata(Action<string, object> add)
    {
        add("enduser.id", this.Id);
        add("enduser.account_id", this.AccountId);
    }
}
