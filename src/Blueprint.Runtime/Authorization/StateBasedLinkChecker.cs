using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// A convenience base class for authorizers that hide operation links based on the current
/// state of a resource. For example, a "Deactivate" link should not appear on an already-deactivated
/// entity, and a "Publish" link should not appear on an already-published document.
/// </summary>
/// <remarks>
/// <para>
/// State-based link checkers do <strong>not</strong> affect operation execution — calling the
/// operation directly will still succeed (or fail via normal validation). The checker only
/// controls whether the link is <em>visible</em> in API responses.
/// </para>
/// <para>
/// Register implementations via DI as <c>ILinkVisibilityAuthoriser</c>:
/// </para>
/// <code>
/// services.AddScoped&lt;ILinkVisibilityAuthoriser, DeactivateLinkChecker&gt;();
/// </code>
/// </remarks>
/// <typeparam name="TOperation">The operation type whose link visibility is being controlled.</typeparam>
/// <typeparam name="TResource">The resource type whose state determines link visibility.</typeparam>
public abstract class StateBasedLinkChecker<TOperation, TResource> : ILinkVisibilityAuthoriser
{
    // ReSharper disable once StaticMemberInGenericType — intentional per-closed-generic caching
    private static readonly Task<ExecutionAllowed> _stateCheckFailed = Task.FromResult(
        ExecutionAllowed.No(
            "Resource in incorrect state",
            "Cannot perform operation at this time.",
            ExecutionAllowedFailureType.Authorisation));

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="true"/> only for operations matching <typeparamref name="TOperation"/>.
    /// </remarks>
    public bool AppliesTo(ApiOperationDescriptor descriptor)
    {
        return descriptor.OperationType == typeof(TOperation);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When <paramref name="resource"/> is assignable to <typeparamref name="TResource"/> and
    /// <see cref="IsLinkAvailableForOperation"/> returns <see langword="false"/>, the link is hidden
    /// from the API response.
    /// </remarks>
    public Task<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object resource)
    {
        if (resource is TResource r && !this.IsLinkAvailableForOperation(operationContext, r))
        {
            return _stateCheckFailed;
        }

        return ExecutionAllowed.YesTask;
    }

    /// <summary>
    /// Determines whether the operation link should be visible given the current state of
    /// the resource. Return <see langword="false"/> to hide the link.
    /// </summary>
    /// <param name="operationContext">The context of the currently executing operation.</param>
    /// <param name="resource">The resource whose state controls link visibility.</param>
    /// <returns>
    /// <see langword="true"/> if the link should be shown; <see langword="false"/> to hide it.
    /// </returns>
    protected abstract bool IsLinkAvailableForOperation(ApiOperationContext operationContext, TResource resource);
}
