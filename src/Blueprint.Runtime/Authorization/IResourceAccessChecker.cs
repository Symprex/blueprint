using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Decides whether a caller holds a grant covering a <see cref="ResourceDemand"/>. It is what
/// <see cref="ResourceKeyAuthorizationProvider"/> asks for each scope pair, and what app code asks directly when it must
/// decide access to a resource itself, such as a handler filtering what it returns by what the caller may act on.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddResourceKeyAuthorization</c> registers the default implementation, scoped because the
/// <see cref="IResourceKeyExpander"/>s and <see cref="IResourceGrantSource"/>s behind it may be. Resolve it from the
/// operation's own services, as the provider does.
/// </para>
/// <para>
/// The default implementation decides as follows:
/// </para>
/// <list type="number">
///   <item>An absolute key is used as it is. A partial key is expanded by the first <see cref="IResourceKeyExpander"/>
///   that handles it, and denied, with a reason naming the key, when none does.</item>
///   <item>The grants of every registered <see cref="IResourceGrantSource"/> are combined.</item>
///   <item>The demand is allowed when any grant covers any of the keys, by <see cref="ResourceGrant.Covers"/>.</item>
/// </list>
/// <para>
/// It decides grants only. It does not refuse an unauthenticated or inactive caller: the provider does that before asking
/// it, and app code asking it directly runs inside an operation whose caller has already been authenticated.
/// </para>
/// </remarks>
public interface IResourceAccessChecker
{
    /// <summary>
    /// Decides whether <paramref name="principal"/> holds a grant covering <paramref name="demand"/>.
    /// </summary>
    /// <param name="context">The context of the operation executing, whose services expanders and grant sources may use.</param>
    /// <param name="principal">The caller.</param>
    /// <param name="demand">The operation and the key demanded. Its key, not the scope pair's, is the one decided.</param>
    /// <param name="scopePair">
    /// The scope pair the demand is made under, which selects the grants a source keyed by scope pair returns. App code
    /// asking directly passes the pair the operation declares, which its descriptor's
    /// <see cref="OperationAuthorizationFeatureData"/> holds.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the decision.</param>
    /// <returns>
    /// <see cref="ExecutionAllowed.Yes"/>, or an <see cref="ExecutionAllowedFailureType.Authorisation"/> denial whose reason
    /// names the key and why no grant covers it.
    /// </returns>
    ValueTask<ExecutionAllowed> HasAccessAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        ResourceDemand demand,
        ResolvedScopePair scopePair,
        CancellationToken cancellationToken);
}
