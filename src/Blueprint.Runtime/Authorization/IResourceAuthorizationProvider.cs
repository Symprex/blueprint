using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Abstraction for evaluating resource-level authorization checks at pipeline execution time.
/// Implementations should inspect the authenticated principal's claims, roles, or permissions
/// to determine whether the requested scope pairs are satisfied.
/// </summary>
/// <remarks>
/// <para>
/// The executor resolves this service once from the root <see cref="IServiceProvider"/>, and the generated pipeline
/// calls it for each operation that carries <c>[Resource("noun", "verb")]</c> or <c>[FromResource(...)]</c> metadata.
/// It is required once any operation does. Without it, <c>MapBlueprintApi</c> throws at start-up, and the
/// generated check refuses each such operation with a 403. A missing registration used to skip the check, letting
/// every authenticated caller through (Symprex/blueprint#41).
/// </para>
/// <para>
/// Scope pairs use any-of semantics: the caller is authorized if <em>any</em> of the declared
/// scope pairs is satisfied. This matches the legacy Blueprint convention where multiple
/// <c>[Resource]</c> attributes on a single operation represent alternative permission paths.
/// </para>
/// <para>
/// Each <see cref="ResolvedScopePair"/> carries the noun, verb, optional original template, and
/// the compile-time–expanded resource key. When <see cref="ResolvedScopePair.ExpandedResourceKey"/>
/// is <see langword="null"/>, no template was declared (noun+verb only).
/// </para>
/// <para>
/// The <see cref="ApiOperationContext"/> parameter provides access to the operation instance
/// and its properties, enabling ownership-based authorization patterns where the provider
/// inspects operation properties (e.g. <c>TenantId</c>) or claim values to determine whether
/// the caller owns the requested resource.
/// </para>
/// </remarks>
public interface IResourceAuthorizationProvider
{
    /// <summary>
    /// Evaluates whether the <paramref name="principal"/> satisfies at least one of the
    /// supplied <paramref name="resolvedScopePairs"/> for the current operation.
    /// </summary>
    /// <param name="operationContext">
    /// The current operation execution context, providing access to the operation instance,
    /// its descriptor, and the scoped service provider. Implementations can inspect operation
    /// properties for ownership-based authorization (e.g. checking that the caller's tenant
    /// claim matches the operation's <c>TenantId</c> property).
    /// </param>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="resolvedScopePairs">
    /// The resource scope pairs declared on the operation, with compile-time–expanded resource
    /// keys. Any-of semantics: return <see langword="true"/> if at least one pair is satisfied.
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
