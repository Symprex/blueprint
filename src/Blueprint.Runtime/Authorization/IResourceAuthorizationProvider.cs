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
/// The executor resolves every registered provider once from the root <see cref="IServiceProvider"/>, and the generated
/// pipeline asks them for each operation that carries <c>[Resource("noun", "verb")]</c> or <c>[FromResource(...)]</c>
/// metadata. At least one is required once any operation does. Without one, <c>MapBlueprintApi</c> throws at start-up,
/// and the generated check refuses each such operation with a 403. A missing registration used to skip the check,
/// letting every authenticated caller through (Symprex/blueprint#41).
/// </para>
/// <para>
/// <b>Every registered provider must allow</b>, as every applicable <see cref="IApiAuthoriser"/> must: they are asked in
/// registration order and the first denial is the answer, so the rest are not asked. Register one provider per concern,
/// for example one that checks a token's OAuth scopes and one that checks the caller's grants on the resource, rather than
/// one that composes both.
/// </para>
/// <para>
/// Within one provider, scope pairs use any-of semantics: the caller is authorized if <em>any</em> of the declared
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
    /// Decides whether the <paramref name="principal"/> satisfies at least one of the request's scope pairs.
    /// </summary>
    /// <param name="operationContext">
    /// The current operation execution context, providing access to the operation instance,
    /// its descriptor, and the scoped service provider. Implementations can inspect operation
    /// properties for ownership-based authorization (e.g. checking that the caller's tenant
    /// claim matches the operation's <c>TenantId</c> property).
    /// </param>
    /// <param name="principal">The caller's claims principal.</param>
    /// <param name="request">
    /// The scope pairs declared on the operation, with their resource keys resolved, and why the provider is being asked.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns>
    /// <see cref="ExecutionAllowed.Yes"/> to allow, or a denial made with <see cref="ExecutionAllowed.No"/>. A denial's
    /// <see cref="ExecutionAllowed.FailureType"/> selects the response, exactly as an authoriser's does: a 403 for
    /// <see cref="ExecutionAllowedFailureType.Authorisation"/> and a 401 otherwise. Its
    /// <see cref="ExecutionAllowed.Message"/> titles the response, and its <see cref="ExecutionAllowed.Reason"/> is
    /// logged and never written.
    /// </returns>
    ValueTask<ExecutionAllowed> AuthorizeAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        ResourceAuthorizationRequest request,
        CancellationToken cancellationToken);
}
