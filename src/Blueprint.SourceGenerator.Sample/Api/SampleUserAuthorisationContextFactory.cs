using System.Security.Claims;
using Blueprint.Authorization;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates how a consumer converts ASP.NET Core's <see cref="ClaimsIdentity"/> into an
/// application-specific <see cref="IUserAuthorisationContext"/> used by resource authorization and handlers.
/// </summary>
/// <remarks>
/// In a real Signature365-style consumer, this would query the user/account store or decode
/// a JWT to build the full user context. This sample extracts standard identity and account claims.
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
        var context = new SampleUserContext(userId, accountId, email, name);
        logger.LogDebug("Created user context: {UserId} / {AccountId}", userId, accountId);

        return Task.FromResult<IUserAuthorisationContext?>(context);
    }
}

/// <summary>
/// A minimal <see cref="IUserAuthorisationContext"/> implementation that carries the user's identity
/// and account information extracted from JWT claims for resource authorization and auditing flows.
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

    /// <inheritdoc />
    public void PopulateMetadata<TState>(Action<string, object, TState> add, TState state)
    {
        add("enduser.id", this.Id, state);
        add("enduser.account_id", this.AccountId, state);
    }
}
