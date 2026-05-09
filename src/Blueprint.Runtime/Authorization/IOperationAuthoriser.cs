using Blueprint.Model;
using Blueprint.Runtime.Exec;

using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// A focused authorization interface for checking whether an operation can be <strong>executed</strong>.
/// Implementations that only need to guard execution (and do not participate in link-visibility
/// decisions) should implement this interface rather than <see cref="IApiAuthoriser"/>.
/// </summary>
/// <remarks>
/// <para>
/// The generated pipeline resolves all registered <see cref="IOperationAuthoriser"/> and
/// <see cref="IApiAuthoriser"/> implementations from the scoped <see cref="IServiceProvider"/>
/// and evaluates those whose <see cref="AppliesTo"/> returns <see langword="true"/>.
/// A denial from any authoriser short-circuits the pipeline.
/// </para>
/// <para>
/// If an authoriser needs to participate in <em>both</em> execution and link-visibility checks,
/// implement <see cref="IApiAuthoriser"/> instead — it extends both
/// <see cref="IOperationAuthoriser"/> and <see cref="ILinkVisibilityAuthoriser"/>.
/// </para>
/// </remarks>
[PublicAPI]
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IOperationAuthoriser
{
    /// <summary>
    /// Determines whether this authoriser applies to the given operation.
    /// </summary>
    /// <param name="descriptor">The operation descriptor to check.</param>
    /// <returns><see langword="true"/> if this authoriser should be evaluated for the operation.</returns>
    bool AppliesTo(ApiOperationDescriptor descriptor);

    /// <summary>
    /// Evaluates whether the current user is authorized to execute the specified operation.
    /// </summary>
    /// <param name="operationContext">The current operation execution context.</param>
    /// <param name="descriptor">The descriptor for the operation being executed.</param>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>
    /// <see cref="ExecutionAllowed.Yes"/> if the operation should proceed, or a denial
    /// created via <see cref="ExecutionAllowed.No"/> to short-circuit the pipeline.
    /// </returns>
    Task<ExecutionAllowed> CanExecuteOperationAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object operation);
}
