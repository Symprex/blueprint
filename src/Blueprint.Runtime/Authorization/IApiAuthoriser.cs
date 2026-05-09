using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Defines a pluggable authorization check that participates in both operation execution
/// and link-visibility decisions. This is the combined interface — implementations that only
/// need one concern should implement <see cref="IOperationAuthoriser"/> or
/// <see cref="ILinkVisibilityAuthoriser"/> instead.
/// </summary>
/// <remarks>
/// <para>
/// Consumer applications register <see cref="IApiAuthoriser"/> implementations to enforce
/// business-level authorization rules (e.g. tenant isolation, feature flags, subscription checks).
/// The generated pipeline resolves all registered authorizers from the scoped
/// <see cref="IServiceProvider"/> and calls <see cref="IOperationAuthoriser.CanExecuteOperationAsync"/>
/// for each one that <see cref="IOperationAuthoriser.AppliesTo"/> the current operation.
/// </para>
/// <para>
/// When an authorizer returns <see cref="ExecutionAllowed.No"/>, the pipeline returns either
/// <see cref="UnauthenticatedResult"/> (for <see cref="ExecutionAllowedFailureType.Authentication"/>)
/// or <see cref="ForbiddenResult"/> (for <see cref="ExecutionAllowedFailureType.Authorisation"/>).
/// </para>
/// </remarks>
public interface IApiAuthoriser : IOperationAuthoriser, ILinkVisibilityAuthoriser
{
    /// <summary>
    /// Determines whether this authorizer applies to the given operation. This satisfies
    /// both <see cref="IOperationAuthoriser.AppliesTo"/> and <see cref="ILinkVisibilityAuthoriser.AppliesTo"/>.
    /// </summary>
    /// <param name="descriptor">The operation descriptor to check.</param>
    /// <returns><see langword="true"/> if this authorizer should be evaluated for the operation.</returns>
    new bool AppliesTo(ApiOperationDescriptor descriptor);
}
