using System.Security.Claims;

namespace Blueprint.Testing;

/// <summary>
/// Manages authentication state for test scenarios, providing scoped auth contexts
/// that automatically apply to <see cref="BlueprintTestContext"/> configurations.
/// <para>
/// <strong>S365 compatibility</strong>: this class provides both the original
/// <see cref="SetDefaultUser(ClaimsPrincipal)"/> / <see cref="AsUser(ClaimsPrincipal)"/> API
/// and the S365 <c>Authentication</c> shape — <see cref="Set(ClaimsIdentity)"/>,
/// <see cref="As(ClaimsIdentity)"/>, <see cref="MakeAnonymous"/>. Migrating test helpers
/// can call either surface without code changes.
/// </para>
/// </summary>
public sealed class TestAuthenticationManager
{
    private ClaimsPrincipal? _currentPrincipal;
    private ClaimsPrincipal? _defaultPrincipal;

    /// <summary>
    /// Gets the currently active <see cref="ClaimsPrincipal"/>, falling back to the default principal.
    /// Returns <c>null</c> when anonymous (no identity).
    /// </summary>
    public ClaimsPrincipal? CurrentPrincipal => this._currentPrincipal ?? this._defaultPrincipal;

    // ──────────────────────────────────────────────
    //  Original Blueprint API
    // ──────────────────────────────────────────────

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

    // ──────────────────────────────────────────────
    //  S365 Authentication compatibility API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Sets the ambient identity from a <see cref="ClaimsIdentity"/>. This matches the S365
    /// <c>Authentication.Set(ClaimsIdentity)</c> pattern used by <c>AuthenticateAs*</c> helpers.
    /// </summary>
    /// <param name="identity">The identity to set as the ambient user.</param>
    public void Set(ClaimsIdentity identity)
    {
        this._defaultPrincipal = new ClaimsPrincipal(identity);
        this._currentPrincipal = null;
    }

    /// <summary>
    /// Sets the ambient identity from a <see cref="ClaimsPrincipal"/>. Passing <c>null</c> clears
    /// the ambient identity (equivalent to <see cref="MakeAnonymous"/>). This matches the S365
    /// <c>Authentication.Set(ClaimsPrincipal?)</c> pattern.
    /// </summary>
    /// <param name="principal">The principal to set, or <c>null</c> to go anonymous.</param>
    public void Set(ClaimsPrincipal? principal)
    {
        this._defaultPrincipal = principal;
        this._currentPrincipal = null;
    }

    /// <summary>
    /// Temporarily switches to the specified identity. Dispose the returned scope to restore
    /// the previous state. This matches the S365 <c>Authentication.As(ClaimsIdentity)</c> pattern
    /// used by <c>PerformOperationAsUserAsync</c>.
    /// </summary>
    /// <param name="identity">The temporary identity.</param>
    /// <returns>A disposable scope that restores the previous identity on disposal.</returns>
    public IDisposable As(ClaimsIdentity identity)
    {
        return this.AsUser(new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// Permanently clears the ambient identity, making all subsequent operations anonymous.
    /// This matches the S365 <c>Authentication.MakeAnonymous()</c> pattern.
    /// </summary>
    public void MakeAnonymous()
    {
        this._defaultPrincipal = null;
        this._currentPrincipal = null;
    }

    // ──────────────────────────────────────────────
    //  Shared API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Applies the current authentication state to the given <see cref="BlueprintTestContext"/>.
    /// Called automatically by <see cref="BlueprintTestFixture{TApp,TExec}"/>; can also be called
    /// manually from the <c>configure</c> callback.
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
