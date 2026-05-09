using Blueprint.Model;
using Blueprint.Runtime.Exec;

using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// A focused authorization interface for checking whether an operation's <strong>link</strong>
/// should be visible in API responses. Implementations that only need to control link visibility
/// (e.g. state-based checks) should implement this interface rather than <see cref="IApiAuthoriser"/>.
/// </summary>
/// <remarks>
/// <para>
/// Link-visibility authorisers are evaluated during response link population.
/// The <see cref="IApiAuthoriserAggregator"/> resolves all registered
/// <see cref="ILinkVisibilityAuthoriser"/> and <see cref="IApiAuthoriser"/>
/// implementations and short-circuits on the first denial.
/// </para>
/// <para>
/// <see cref="StateBasedLinkChecker{TOperation,TResource}"/> implements this interface
/// and is the recommended base class for state-based link visibility checks.
/// </para>
/// <para>
/// If an authoriser needs to participate in <em>both</em> execution and link-visibility checks,
/// implement <see cref="IApiAuthoriser"/> instead — it extends both
/// <see cref="IOperationAuthoriser"/> and <see cref="ILinkVisibilityAuthoriser"/>.
/// </para>
/// </remarks>
[PublicAPI]
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface ILinkVisibilityAuthoriser
{
    /// <summary>
    /// Determines whether this authoriser applies to the given operation.
    /// </summary>
    /// <param name="descriptor">The operation descriptor to check.</param>
    /// <returns><see langword="true"/> if this authoriser should be evaluated for the operation.</returns>
    bool AppliesTo(ApiOperationDescriptor descriptor);

    /// <summary>
    /// Evaluates whether a link to the specified operation should be visible for the given resource.
    /// Used during link generation to hide links the user cannot follow.
    /// </summary>
    /// <param name="operationContext">The operation context of the currently executing operation.</param>
    /// <param name="descriptor">The descriptor of the linked operation.</param>
    /// <param name="resource">The resource being returned (the link's context object).</param>
    /// <returns>
    /// <see cref="ExecutionAllowed.Yes"/> if the link should be shown, or a denial to hide it.
    /// </returns>
    Task<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object resource);
}
