using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Answers which resource grants a principal holds for one operation: the store of grants behind
/// <see cref="IResourceAccessChecker"/>. Register one with <c>AddResourceGrantSource</c>.
/// </summary>
/// <remarks>
/// <para>
/// A source is given both the operation and the scope pair being satisfied, so a source keyed by either fits: one that
/// reads grants assigned per scope pair, as <see cref="ScopePairClaimResourceGrantSource"/> does, or one that reads grants
/// assigned per operation type.
/// </para>
/// <para>
/// The grants of every registered source are combined: a demand is satisfied when a grant from any source covers it, so
/// once one source's grants do, the sources after it are not asked. A source that holds nothing for the operation returns
/// <see cref="ResourceGrants.Empty"/>, never <see langword="null"/>.
/// </para>
/// <para>
/// A source built from stored or issued data should parse grants with <see cref="ResourceGrant.TryParse"/> and skip any
/// that fail, so that one malformed grant refuses nothing more than itself instead of failing every request its holder
/// makes.
/// </para>
/// </remarks>
public interface IResourceGrantSource
{
    /// <summary>
    /// Gets the grants <paramref name="principal"/> holds for <paramref name="operation"/> under
    /// <paramref name="scopePair"/>.
    /// </summary>
    /// <param name="context">The context of the operation executing, which for a link is the one whose response carries it.</param>
    /// <param name="principal">The caller.</param>
    /// <param name="operation">The operation being authorised, which for a link is the linked operation.</param>
    /// <param name="scopePair">
    /// The scope pair being satisfied. Its <see cref="ResolvedScopePair.ExpandedResourceKey"/> is not the key being decided;
    /// the demand's key is, after any expansion, so a source must not filter by it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the lookup.</param>
    /// <returns>The grants held, or <see cref="ResourceGrants.Empty"/>.</returns>
    ValueTask<ResourceGrants> GetGrantsAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        ApiOperationDescriptor operation,
        ResolvedScopePair scopePair,
        CancellationToken cancellationToken);
}
