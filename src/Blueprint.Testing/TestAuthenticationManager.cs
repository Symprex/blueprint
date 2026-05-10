using System.Security.Claims;

namespace Blueprint.Testing;

/// <summary>
/// Manages authentication state for test scenarios, providing scoped auth contexts
/// that automatically apply to <see cref="BlueprintTestContext"/> configurations.
/// <para>
/// Typical usage: create an instance in a test fixture, set the default identity, and
/// use <see cref="AsAnonymous"/> or <see cref="AsUser(ClaimsPrincipal)"/> to temporarily change identity
/// within a <c>using</c> block.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var auth = new TestAuthenticationManager();
/// auth.SetDefaultUser(claims => claims.Add(new Claim("sub", "user-1")));
///
/// // Uses default user
/// await host.ExecuteAsync(query, ctx => auth.Apply(ctx));
///
/// // Temporarily anonymous
/// using (auth.AsAnonymous())
/// {
///     await host.ExecuteAsync(query, ctx => auth.Apply(ctx));
/// }
/// </code>
/// </example>
public sealed class TestAuthenticationManager
{
    private ClaimsPrincipal? _currentPrincipal;
    private ClaimsPrincipal? _defaultPrincipal;

    /// <summary>
    /// Gets the currently active <see cref="ClaimsPrincipal"/>, falling back to the default principal.
    /// Returns <c>null</c> when anonymous (no identity).
    /// </summary>
    public ClaimsPrincipal? CurrentPrincipal => this._currentPrincipal ?? this._defaultPrincipal;

    /// <summary>
    /// Sets the default user identity that applies when no scoped override is active.
    /// </summary>
    /// <param name="principal">The default claims principal.</param>
    public void SetDefaultUser(ClaimsPrincipal principal)
    {
        this._defaultPrincipal = principal;
    }

    /// <summary>
    /// Sets the default user from a set of claims with the specified authentication type.
    /// </summary>
    /// <param name="configureClaims">A callback that receives a list of claims to populate.</param>
    /// <param name="authenticationType">
    /// The authentication type. Must be non-null for <see cref="ClaimsIdentity.IsAuthenticated"/>
    /// to return <c>true</c>. Defaults to <c>"TestAuth"</c>.
    /// </param>
    public void SetDefaultUser(
        Action<IList<Claim>> configureClaims,
        string authenticationType = "TestAuth")
    {
        var claims = new List<Claim>();
        configureClaims(claims);
        var identity = new ClaimsIdentity(claims, authenticationType);
        this.SetDefaultUser(new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// Applies the current authentication state to the given <see cref="BlueprintTestContext"/>.
    /// Call this from the <c>configure</c> callback of <see cref="BlueprintTestHost{TApp,TExec}.ExecuteAsync"/>.
    /// </summary>
    /// <param name="context">The test context to apply authentication to.</param>
    public void Apply(BlueprintTestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = this.CurrentPrincipal;
        if (principal is not null)
        {
            context.WithUser(principal);
        }
    }

    /// <summary>
    /// Temporarily switches to anonymous (unauthenticated) access. Dispose the returned
    /// scope to restore the previous identity.
    /// </summary>
    /// <returns>A disposable scope that restores the previous identity on disposal.</returns>
    public IDisposable AsAnonymous()
    {
        var previous = this._currentPrincipal;
        this._currentPrincipal = null;

        // Clear the default too so CurrentPrincipal returns null
        var previousDefault = this._defaultPrincipal;
        this._defaultPrincipal = null;

        return new AuthScope(() =>
        {
            this._currentPrincipal = previous;
            this._defaultPrincipal = previousDefault;
        });
    }

    /// <summary>
    /// Temporarily switches to the specified user. Dispose the returned scope to restore
    /// the previous identity.
    /// </summary>
    /// <param name="principal">The temporary user identity.</param>
    /// <returns>A disposable scope that restores the previous identity on disposal.</returns>
    public IDisposable AsUser(ClaimsPrincipal principal)
    {
        var previous = this._currentPrincipal;
        this._currentPrincipal = principal;
        return new AuthScope(() => this._currentPrincipal = previous);
    }

    /// <summary>
    /// Temporarily switches to a user built from claims. Dispose the returned scope to restore
    /// the previous identity.
    /// </summary>
    /// <param name="configureClaims">A callback that receives a list of claims to populate.</param>
    /// <param name="authenticationType">The authentication type. Defaults to <c>"TestAuth"</c>.</param>
    /// <returns>A disposable scope that restores the previous identity on disposal.</returns>
    public IDisposable AsUser(
        Action<IList<Claim>> configureClaims,
        string authenticationType = "TestAuth")
    {
        var claims = new List<Claim>();
        configureClaims(claims);
        var identity = new ClaimsIdentity(claims, authenticationType);
        return this.AsUser(new ClaimsPrincipal(identity));
    }

    private sealed class AuthScope : IDisposable
    {
        private readonly Action _restore;
        private bool _disposed;

        public AuthScope(Action restore)
        {
            this._restore = restore;
        }

        public void Dispose()
        {
            if (!this._disposed)
            {
                this._restore();
                this._disposed = true;
            }
        }
    }
}
