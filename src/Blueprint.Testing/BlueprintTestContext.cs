using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Runtime.Exec;

namespace Blueprint.Testing;

/// <summary>
/// Provides a fluent configuration surface for customizing an operation execution context
/// before the pipeline runs. Primarily used to set up authentication, user identity, and
/// per-execution overrides in test scenarios.
/// </summary>
public sealed class BlueprintTestContext
{
    private readonly ApiOperationContext _context;

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintTestContext"/> class.
    /// </summary>
    /// <param name="context">The operation context to configure.</param>
    /// <param name="scopedServiceProvider">The scoped service provider for this execution.</param>
    internal BlueprintTestContext(ApiOperationContext context, IServiceProvider scopedServiceProvider)
    {
        this._context = context;
        this.ScopedServiceProvider = scopedServiceProvider;
    }

    /// <summary>
    /// Gets the scoped service provider for this execution. Useful for resolving
    /// test doubles or verifying scoped state after execution.
    /// </summary>
    public IServiceProvider ScopedServiceProvider { get; }

    /// <summary>
    /// Gets the underlying <see cref="ApiOperationContext"/> for advanced configuration.
    /// Prefer the typed helper methods on this class when possible.
    /// </summary>
    public ApiOperationContext OperationContext => this._context;

    /// <summary>
    /// Sets the <see cref="ClaimsPrincipal"/> for the operation, simulating an authenticated user.
    /// The principal is set on both the operation context and the underlying <see cref="HttpContext"/>
    /// so generated authentication checks see the same identity.
    /// </summary>
    /// <param name="principal">The claims principal to use.</param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithUser(ClaimsPrincipal principal)
    {
        this._context.UserPrincipal = principal;

        if (this._context.HttpContext is { } httpContext)
        {
            httpContext.User = principal;
        }

        return this;
    }

    /// <summary>
    /// Creates a <see cref="ClaimsPrincipal"/> from a set of claims and assigns it to the context.
    /// This is a convenience overload for the common case of building a simple authenticated identity.
    /// </summary>
    /// <param name="configureClaims">A callback that receives a list of claims to populate.</param>
    /// <param name="authenticationType">
    /// The authentication type for the <see cref="ClaimsIdentity"/>. Defaults to <c>"TestAuth"</c>.
    /// Must be non-null for <see cref="ClaimsIdentity.IsAuthenticated"/> to return <c>true</c>.
    /// </param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithUser(
        Action<IList<System.Security.Claims.Claim>> configureClaims,
        string authenticationType = "TestAuth")
    {
        var claims = new List<System.Security.Claims.Claim>();
        configureClaims(claims);
        var identity = new ClaimsIdentity(claims, authenticationType);
        return this.WithUser(new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// Sets the application-specific <see cref="IUserAuthorisationContext"/> directly,
    /// bypassing the <see cref="IUserAuthorisationContextFactory"/>. Use this when tests
    /// need to inject a specific authorization context without wiring up the factory.
    /// </summary>
    /// <param name="authorisationContext">The authorization context to use.</param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithAuthorisationContext(IUserAuthorisationContext authorisationContext)
    {
        this._context.UserAuthorisationContext = authorisationContext;
        return this;
    }

    /// <summary>
    /// Skips all authorization checks for this operation execution. Useful for tests
    /// that focus on handler logic and don't need to verify authorization behavior.
    /// </summary>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext SkipAuthorisation()
    {
        this._context.SkipAuthorisation = true;
        return this;
    }

    /// <summary>
    /// Stores an arbitrary key-value pair in the context's data dictionary.
    /// Useful for injecting test state that middleware or handlers read from context.
    /// </summary>
    /// <param name="key">The data key.</param>
    /// <param name="value">The data value.</param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithData(string key, object value)
    {
        this._context.Data[key] = value;
        return this;
    }

    /// <summary>
    /// Convenience method that creates an authenticated user with the specified permission claims.
    /// This matches the S365 pattern where permissions are stored as claims with a configurable
    /// claim type (defaulting to <c>"permission"</c>).
    /// <para>
    /// Use this when testing authorization flows that depend on <c>[Permission]</c> or
    /// <c>[PermissionFor]</c> attributes. Each permission string becomes a separate claim.
    /// </para>
    /// </summary>
    /// <param name="permissions">The permission identifiers to grant the test user.</param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithPermissions(params string[] permissions)
    {
        return this.WithPermissions("permission", permissions);
    }

    /// <summary>
    /// Convenience method that creates an authenticated user with permissions stored under a custom claim type.
    /// </summary>
    /// <param name="claimType">The claim type used for permission storage (e.g., <c>"permission"</c>, <c>"scope"</c>).</param>
    /// <param name="permissions">The permission identifiers to grant the test user.</param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithPermissions(string claimType, params string[] permissions)
    {
        var claims = new List<System.Security.Claims.Claim>(permissions.Length + 1)
        {
            new("sub", "test-user-" + Guid.NewGuid().ToString("N")[..8]),
        };

        foreach (var permission in permissions)
        {
            claims.Add(new System.Security.Claims.Claim(claimType, permission));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return this.WithUser(new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// Convenience method that creates an authenticated user with a tenant claim and optional permissions.
    /// This matches the S365 pattern where every user belongs to a tenant identified by an account/tenant ID.
    /// <para>
    /// The generated user will have <c>sub</c>, <c>tenant_id</c> (or custom claim type), and
    /// optional permission claims. Property populators that read tenant context from JWT claims
    /// will see the tenant claim automatically.
    /// </para>
    /// </summary>
    /// <param name="tenantId">The tenant identifier to assign.</param>
    /// <param name="permissions">Optional permissions to include.</param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithTenant(string tenantId, params string[] permissions)
    {
        return this.WithTenant("tenant_id", tenantId, permissions);
    }

    /// <summary>
    /// Convenience method that creates an authenticated user with a custom tenant claim type and optional permissions.
    /// </summary>
    /// <param name="tenantClaimType">The claim type for the tenant identifier (e.g., <c>"tenant_id"</c>, <c>"account_id"</c>).</param>
    /// <param name="tenantId">The tenant identifier value.</param>
    /// <param name="permissions">Optional permissions to include.</param>
    /// <returns>This context for chaining.</returns>
    public BlueprintTestContext WithTenant(string tenantClaimType, string tenantId, params string[] permissions)
    {
        var claims = new List<System.Security.Claims.Claim>(permissions.Length + 2)
        {
            new("sub", "test-user-" + Guid.NewGuid().ToString("N")[..8]),
            new(tenantClaimType, tenantId),
        };

        foreach (var permission in permissions)
        {
            claims.Add(new System.Security.Claims.Claim("permission", permission));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return this.WithUser(new ClaimsPrincipal(identity));
    }
}
