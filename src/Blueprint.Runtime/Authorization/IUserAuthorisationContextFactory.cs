using System.Security.Claims;

namespace Blueprint.Authorization;

/// <summary>
/// Factory interface for creating an <see cref="IUserAuthorisationContext"/> from the authenticated
/// user's <see cref="ClaimsIdentity"/>. Implementations translate identity provider claims into
/// application-specific user context (e.g. resolving tenant, account status, display name).
/// </summary>
/// <remarks>
/// <para>
/// Register an implementation of this interface in DI to enable the generated pipeline to populate
/// <see cref="Runtime.Exec.ApiOperationContext.UserAuthorisationContext"/>. When not registered,
/// the context remains <see langword="null"/> and authorizers that inspect it should handle that
/// case gracefully.
/// </para>
/// <para>
/// The factory is invoked in the <c>Authentication</c> pipeline stage after the identity check
/// passes, ensuring that only authenticated users trigger context creation. Anonymous operations
/// (marked with <c>[AllowAnonymous]</c>) skip the factory entirely.
/// </para>
/// </remarks>
public interface IUserAuthorisationContextFactory
{
    /// <summary>
    /// Creates an <see cref="IUserAuthorisationContext"/> from the authenticated user's identity.
    /// The generated pipeline calls this overload. The default implementation dispatches to the
    /// single-parameter overload for migration compatibility with legacy implementations.
    /// </summary>
    /// <param name="identity">The authenticated user's claims identity (never <see langword="null"/>).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// An application-specific authorization context for the current user, or <see langword="null"/>
    /// if the identity cannot be resolved to a valid user (which will cause a 401 response).
    /// </returns>
    Task<IUserAuthorisationContext?> CreateContextAsync(
        ClaimsIdentity identity,
        CancellationToken cancellationToken)
    {
        return this.CreateContextAsync(identity);
    }

    /// <summary>
    /// Legacy single-parameter overload for migration compatibility. Implement either this or the
    /// two-parameter overload with <see cref="CancellationToken"/>.
    /// </summary>
    /// <param name="identity">The authenticated user's claims identity (never <see langword="null"/>).</param>
    /// <returns>
    /// An application-specific authorization context for the current user, or <see langword="null"/>
    /// if the identity cannot be resolved to a valid user (which will cause a 401 response).
    /// </returns>
    Task<IUserAuthorisationContext?> CreateContextAsync(ClaimsIdentity identity)
    {
        throw new NotImplementedException(
            "Implement either CreateContextAsync(ClaimsIdentity, CancellationToken) or CreateContextAsync(ClaimsIdentity).");
    }
}
