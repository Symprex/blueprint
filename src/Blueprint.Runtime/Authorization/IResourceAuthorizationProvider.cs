using System.Security.Claims;
using Blueprint.Model;

namespace Blueprint.Authorization;

/// <summary>
/// Abstraction for evaluating resource-level authorization checks at pipeline execution time.
/// Implementations should inspect the authenticated principal's claims, roles, or permissions
/// to determine whether the requested scope pairs are satisfied.
/// </summary>
/// <remarks>
/// <para>
/// The generated pipeline resolves this service from the scoped <see cref="IServiceProvider"/>
/// when an operation carries <c>[Resource("noun", "verb")]</c> or <c>[FromResource(...)]</c> metadata.
/// If the service is not registered, the authorization check is skipped (permissive by default
/// so consumers can adopt authorization incrementally).
/// </para>
/// <para>
/// Scope pairs use any-of semantics: the caller is authorized if <em>any</em> of the declared
/// scope pairs is satisfied. This matches the legacy Blueprint convention where multiple
/// <c>[Resource]</c> attributes on a single operation represent alternative permission paths.
/// </para>
/// </remarks>
public interface IResourceAuthorizationProvider
{
    /// <summary>
    /// Evaluates whether the <paramref name="principal"/> satisfies at least one of the
    /// supplied <paramref name="requiredScopePairs"/> for the current operation.
    /// </summary>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="requiredScopePairs">
    /// The resource scope pairs declared on the operation. Any-of semantics:
    /// return <see langword="true"/> if at least one pair is satisfied.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns>
    /// <see langword="true"/> if the caller is authorized; <see langword="false"/> if the
    /// request should be rejected with a 403 Forbidden response.
    /// </returns>
    Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal principal,
        IReadOnlyList<ScopePair> requiredScopePairs,
        CancellationToken cancellationToken);
}
