using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Builds the shared descriptor-planning model that descriptor emission, app validation, and inspection artifacts can all
/// reuse without diverging in contributor composition or metadata projection.
/// </summary>
internal static class DescriptorPlanBuilder
{
    /// <summary>
    /// Builds a descriptor plan for the requested operation using the currently configured contributor execution plan and
    /// exception-translation policy.
    /// </summary>
    /// <param name="operationFullClassName">The fully qualified metadata name of the operation implementation.</param>
    /// <param name="compilation">The Roslyn compilation that contains the operation and contributor metadata.</param>
    /// <param name="executionPlanResult">The configured contributor execution plan.</param>
    /// <param name="exceptionTranslatorPolicyResult">The resolved exception-translation policy for the active generator configuration.</param>
    /// <returns>A successful descriptor plan or the diagnostics that prevented one from being built.</returns>
    public static Result<DescriptorPlan> Build(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        Result<PipelineExceptionTranslatorPolicy> exceptionTranslatorPolicyResult)
    {
        return Build(operationFullClassName, compilation, executionPlanResult, exceptionTranslatorPolicyResult, operationLookup: null);
    }

    /// <summary>
    /// Builds a descriptor plan reusing a pre-built <see cref="CompilationOperationLookup"/> to avoid
    /// redundant self-link cache construction when building plans for many operations in the same compilation.
    /// </summary>
    /// <param name="operationFullClassName">The fully qualified metadata name of the operation implementation.</param>
    /// <param name="compilation">The Roslyn compilation that contains the operation and contributor metadata.</param>
    /// <param name="executionPlanResult">The configured contributor execution plan.</param>
    /// <param name="exceptionTranslatorPolicyResult">The resolved exception-translation policy for the active generator configuration.</param>
    /// <param name="operationLookup">
    /// A reusable lookup instance scoped to this compilation. When <see langword="null"/>, a fresh instance is
    /// created per call. Pass a shared instance when building plans for multiple operations in a loop to amortise
    /// the self-link cache build cost across all operations.
    /// </param>
    /// <returns>A successful descriptor plan or the diagnostics that prevented one from being built.</returns>
    public static Result<DescriptorPlan> Build(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        Result<PipelineExceptionTranslatorPolicy> exceptionTranslatorPolicyResult,
        CompilationOperationLookup? operationLookup)
    {
        if (executionPlanResult.Value is null)
        {
            return Result<DescriptorPlan>.Failure(executionPlanResult.Diagnostics);
        }

        if (exceptionTranslatorPolicyResult.Value is null)
        {
            // Shared exception-translation policy diagnostics are reported once by the pipeline generator.
            // Descriptor projection stops quietly here so one invalid policy does not fan out into per-operation duplicates
            // while descriptor contributors still avoid projecting stale failure metadata.
            return Result<DescriptorPlan>.Failure(Array.Empty<DiagnosticInfo>());
        }

        var operationImplementationType = compilation.GetTypeByMetadataName(NormalizeMetadataName(operationFullClassName));
        if (operationImplementationType is null)
        {
            return Result<DescriptorPlan>.Failure(
                new DiagnosticInfo(
                    FoundationModuleDiagnosticDescriptors.MissingOperationType,
                    (Location?)null,
                    [
                        operationFullClassName,
                        "descriptor",
                    ]));
        }

        var operationModel = OperationModel.Create(operationImplementationType);
        var descriptorPlan = new DescriptorPlan(operationModel);
        operationLookup ??= new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlanResult.Value.DescriptorContributors)
        {
            contributor.ContributeToDescriptor(new DescriptorContributorContext(operationModel, descriptorPlan, operationLookup, exceptionTranslatorPolicyResult.Value));
        }

        HostExposureDiagnostics.AddUnsupportedHostDiagnostics(descriptorPlan, executionPlanResult.Value.HostContributors);
        HostExposureDiagnostics.AddConflictingHostDiagnostics(descriptorPlan, executionPlanResult.Value.HostContributors);

        var pipelinePlanResult = PipelinePlanBuilder.Build(operationFullClassName, compilation, executionPlanResult, operationLookup: operationLookup as CompilationOperationLookup);

        var descriptorDiags = descriptorPlan.Diagnostics;
        var pipelineDiags = pipelinePlanResult.Diagnostics;
        var descriptorCount = descriptorDiags.Count;
        var pipelineCount = pipelineDiags.Count;
        var totalCount = descriptorCount + pipelineCount;

        if (totalCount == 0)
        {
            return Result.Success(descriptorPlan);
        }

        // Merge diagnostics into a single pre-sized array — avoids Concat + ToArray overhead
        var allDiagnostics = new DiagnosticInfo[totalCount];
        for (var i = 0; i < descriptorCount; i++)
        {
            allDiagnostics[i] = descriptorDiags[i];
        }

        for (var i = 0; i < pipelineCount; i++)
        {
            allDiagnostics[descriptorCount + i] = pipelineDiags[i];
        }

        var hasErrors = false;
        for (var i = 0; i < totalCount; i++)
        {
            if (allDiagnostics[i].Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            {
                hasErrors = true;
                break;
            }
        }

        if (hasErrors)
        {
            return Result<DescriptorPlan>.Failure(allDiagnostics);
        }

        return new Result<DescriptorPlan>(descriptorPlan, new EquatableList<DiagnosticInfo>(allDiagnostics));
    }

    /// <summary>
    /// Creates the default ordered exception-translation policy for the supplied execution plan.
    /// </summary>
    /// <param name="executionPlanResult">The configured contributor execution plan.</param>
    /// <returns>The ordered policy, or diagnostics if translator ownership overlaps unsafely.</returns>
    public static Result<PipelineExceptionTranslatorPolicy> CreateExceptionTranslatorPolicy(Result<BlueprintModuleExecutionPlan> executionPlanResult)
    {
        return executionPlanResult.Value is null
            ? Result<PipelineExceptionTranslatorPolicy>.Failure(executionPlanResult.Diagnostics)
            : PipelineExceptionTranslatorPolicy.CreateFromContributors(executionPlanResult.Value.ExceptionTranslationContributors);
    }

    private static string NormalizeMetadataName(string metadataName)
    {
        return metadataName.StartsWith("global::", StringComparison.Ordinal)
            ? metadataName.Substring("global::".Length)
            : metadataName;
    }
}
