using Blueprint.Runtime.Exec;
using JetBrains.Annotations;

namespace Blueprint;

/// <summary>
/// Provides the execution pipeline for an operation, typically implemented using the Blueprint source generator that
/// will create a pipeline per operation that is tailored to executing that exact operation, with source generated
/// validation etc. that avoids a) a nested delegate structure that is harder to debug and b) a lot of reflection that
/// can be slow and marks it hard to impossible for AOT compiled application.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IOperationExecutorPipeline
{
    /// <summary>
    /// Executes the operation, returning the result.
    /// </summary>
    /// <param name="context">The context for this single execution of the operation.</param>
    /// <returns>The result of executing the operation.</returns>
    Task<OperationResult> ExecuteAsync(ApiOperationContext context);
}
