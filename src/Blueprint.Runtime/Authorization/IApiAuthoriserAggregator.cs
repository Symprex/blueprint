using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Aggregates all registered <see cref="IApiAuthoriser"/> and <see cref="ILinkVisibilityAuthoriser"/>
/// implementations and provides a single entry point for checking link visibility. This is used by
/// link generation and auth-manifest operations.
/// </summary>
/// <remarks>
/// <para>
/// The aggregator short-circuits on the first denial — if any authorizer denies the link,
/// the link is hidden.
/// </para>
/// </remarks>
public interface IApiAuthoriserAggregator
{
    /// <summary>
    /// Checks all registered <see cref="IApiAuthoriser"/> implementations to determine whether
    /// a link to the specified operation should be visible for the current user.
    /// </summary>
    /// <param name="operationContext">The context of the currently executing operation.</param>
    /// <param name="descriptor">The descriptor of the operation whose link is being checked.</param>
    /// <param name="resource">The resource instance that the link is attached to, or <c>null</c> for root links.</param>
    /// <returns>
    /// <see cref="ExecutionAllowed.Yes"/> if all authorizers approve; otherwise the first denial result.
    /// </returns>
    ValueTask<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object? resource);
}
