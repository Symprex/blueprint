using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Defines a pluggable authorization check that is evaluated during pipeline execution.
/// Implementations are registered in DI and invoked for every operation whose descriptor
/// matches <see cref="AppliesTo"/>. Multiple authorizers can be registered; all applicable
/// authorizers are evaluated and the first denial short-circuits execution.
/// </summary>
/// <remarks>
/// <para>
/// Consumer applications register <see cref="IApiAuthoriser"/> implementations to enforce
/// business-level authorization rules (e.g. tenant isolation, feature flags, subscription checks).
/// The generated pipeline resolves all registered authorizers from the scoped
/// <see cref="IServiceProvider"/> and calls <see cref="CanExecuteOperationAsync"/> for each
/// one that <see cref="AppliesTo"/> the current operation.
/// </para>
/// <para>
/// When an authorizer returns <see cref="ExecutionAllowed.No"/>, the pipeline throws either
/// <see cref="UnauthorizedException"/> (for <see cref="ExecutionAllowedFailureType.Authentication"/>)
/// or <see cref="ForbiddenException"/> (for <see cref="ExecutionAllowedFailureType.Authorisation"/>).
/// </para>
/// </remarks>
public interface IApiAuthoriser
{
    /// <summary>
    /// Determines whether this authorizer applies to the given operation. Called once per
    /// operation type during pipeline construction to filter the set of authorizers that
    /// will be evaluated at runtime.
    /// </summary>
    /// <param name="descriptor">The operation descriptor to check.</param>
    /// <returns><see langword="true"/> if this authorizer should be evaluated for the operation.</returns>
    bool AppliesTo(ApiOperationDescriptor descriptor);

    /// <summary>
    /// Evaluates whether the current user is authorized to execute the specified operation.
    /// </summary>
    /// <param name="operationContext">The current operation execution context.</param>
    /// <param name="descriptor">The descriptor for the operation being executed.</param>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>
    /// <see cref="ExecutionAllowed.Yes"/> if the operation should proceed, or a denial
    /// created via <see cref="ExecutionAllowed.No"/> to short-circuit with an appropriate exception.
    /// </returns>
    Task<ExecutionAllowed> CanExecuteOperationAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object operation);

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
