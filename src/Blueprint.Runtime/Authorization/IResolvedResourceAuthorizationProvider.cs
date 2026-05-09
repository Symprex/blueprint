using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// An enhanced version of <see cref="IResourceAuthorizationProvider"/> that receives pre-expanded
/// resource keys, eliminating the need for reflection-based template expansion at runtime.
/// </summary>
/// <remarks>
/// <para>
/// When both <see cref="IResolvedResourceAuthorizationProvider"/> and
/// <see cref="IResourceAuthorizationProvider"/> are registered, the generated pipeline prefers
/// the resolved variant. This allows incremental migration: consumers can implement the new
/// interface to benefit from compile-time expansion while the legacy interface continues to work.
/// </para>
/// <para>
/// Each <see cref="ResolvedScopePair"/> carries the original template alongside the expanded key,
/// so providers can log or audit the template pattern while evaluating the concrete key.
/// </para>
/// </remarks>
public interface IResolvedResourceAuthorizationProvider
{
    /// <summary>
    /// Evaluates whether the <paramref name="principal"/> satisfies at least one of the
    /// supplied <paramref name="resolvedScopePairs"/> for the current operation.
    /// </summary>
    /// <param name="operationContext">
    /// The current operation execution context, providing access to the operation instance,
    /// its descriptor, and the scoped service provider.
    /// </param>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="resolvedScopePairs">
    /// The resource scope pairs with pre-expanded resource keys. Any-of semantics:
    /// return <see langword="true"/> if at least one pair is satisfied.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns>
    /// <see langword="true"/> if the caller is authorized; <see langword="false"/> if the
    /// request should be rejected with a 403 Forbidden response.
    /// </returns>
    Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
        CancellationToken cancellationToken);
}
