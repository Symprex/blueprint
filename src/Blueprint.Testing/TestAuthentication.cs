using System.Security.Claims;

namespace Blueprint.Testing;

/// <summary>
/// Manages ambient authentication state for test executions. When a principal is set,
/// all subsequent operations executed through the host will run with that identity
/// unless overridden per-call via <see cref="BlueprintTestContext.WithUser(ClaimsPrincipal)"/>.
/// <para>
/// <strong>S365 migration note</strong>: this replaces the legacy <c>Authentication</c> class
/// on <c>ApiFixture</c>. The same ambient + disposable-scoped pattern is preserved so
/// <c>AuthenticateAs*</c> and <c>using (api.Authentication.As(...))</c> patterns continue to work.
/// </para>
/// </summary>
public sealed class TestAuthentication
{
    /// <summary>
    /// Gets the current ambient principal that will be applied to every operation context.
    /// When <c>null</c>, operations run as anonymous (no auth).
    /// </summary>
    public ClaimsPrincipal? CurrentPrincipal { get; private set; }

    /// <summary>
    /// Permanently sets the ambient authentication to the given identity.
    /// All subsequent operations will run with this identity until changed.
    /// </summary>
    /// <param name="identity">The claims identity to authenticate as.</param>
    public void Set(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        this.CurrentPrincipal = new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Permanently sets the ambient authentication to the given principal.
    /// Pass <c>null</c> to make subsequent operations anonymous.
    /// </summary>
    /// <param name="principal">The claims principal to authenticate as, or <c>null</c> for anonymous.</param>
    public void Set(ClaimsPrincipal? principal)
    {
        this.CurrentPrincipal = principal;
    }

    /// <summary>
    /// Clears the ambient principal, making all subsequent operations anonymous.
    /// </summary>
    public void MakeAnonymous()
    {
        this.CurrentPrincipal = null;
    }

    /// <summary>
    /// Temporarily clears the ambient principal for the duration of the returned disposable scope.
    /// When the scope is disposed, the previous principal is restored.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that restores the previous principal on disposal.</returns>
    public IDisposable AsAnonymous()
    {
        var previous = this.CurrentPrincipal;
        this.CurrentPrincipal = null;
        return new RevertingScope(this, previous);
    }

    /// <summary>
    /// Temporarily sets the ambient principal to the given identity for the duration of the
    /// returned disposable scope. When the scope is disposed, the previous principal is restored.
    /// <para>
    /// Usage: <c>using (host.Authentication.As(identity)) { await host.CommandAsync(...); }</c>
    /// </para>
    /// </summary>
    /// <param name="identity">The claims identity to temporarily authenticate as.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous principal on disposal.</returns>
    public IDisposable As(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var previous = this.CurrentPrincipal;
        this.CurrentPrincipal = new ClaimsPrincipal(identity);
        return new RevertingScope(this, previous);
    }

    /// <summary>
    /// Temporarily sets the ambient principal for the duration of the returned disposable scope.
    /// When the scope is disposed, the previous principal is restored.
    /// </summary>
    /// <param name="principal">The claims principal to temporarily authenticate as.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous principal on disposal.</returns>
    public IDisposable As(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var previous = this.CurrentPrincipal;
        this.CurrentPrincipal = principal;
        return new RevertingScope(this, previous);
    }

    private sealed class RevertingScope(TestAuthentication owner, ClaimsPrincipal? previous) : IDisposable
    {
        public void Dispose()
        {
            owner.CurrentPrincipal = previous;
        }
    }
}
