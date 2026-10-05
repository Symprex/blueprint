using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Builds the shared pipeline plan that contributors later render into generated executor code.
/// </summary>
internal static class PipelinePlanBuilder
{
    /// <summary>
    /// Builds a pipeline plan for the requested operation using the currently configured contributor execution plan.
    /// </summary>
    /// <param name="operationFullClassName">The fully qualified metadata name of the operation implementation.</param>
    /// <param name="compilation">The Roslyn compilation that contains the operation and contributor metadata.</param>
    /// <param name="executionPlanResult">The configured contributor execution plan.</param>
    /// <param name="handler">An optional pre-resolved handler model to reuse when one is already known.</param>
    /// <param name="operationLookup">An optional pre-scoped operation lookup to reuse when one is already known (e.g. scoped to app-scanned assemblies).</param>
    /// <param name="tenantIdTypeName">The fully qualified name of the hosting app's declared <c>TenantId</c> type, if any.</param>
    /// <returns>A successful pipeline plan or the diagnostics that prevented one from being built.</returns>
    public static Result<PipelinePlan> Build(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        OperationHandlerModel? handler = null,
        CompilationOperationLookup? operationLookup = null,
        string? tenantIdTypeName = null)
    {
        if (executionPlanResult.Value is null)
        {
            return Result<PipelinePlan>.Failure(executionPlanResult.Diagnostics);
        }

        var operationImplementationType = compilation.GetTypeByMetadataName(NormalizeMetadataName(operationFullClassName));
        var operationModel = OperationModel.Create(operationImplementationType!);
        var pipelinePlan = new PipelinePlan(operationModel, new VariableRegistry());
        operationLookup ??= new CompilationOperationLookup(compilation);
        var resolvedHandler = ResolveHandlerIfRequired(
            operationImplementationType!,
            compilation,
            executionPlanResult.Value.PipelineContributors,
            handler);

        if (resolvedHandler is { Value: null })
        {
            return Result<PipelinePlan>.Failure(resolvedHandler.Diagnostics);
        }

        // BP200: Warn when a handler method is synchronous — async handlers prevent thread-pool blocking.
        if (resolvedHandler is { Value.IsHandlerAsync: false })
        {
            var handlerLocation = operationImplementationType!.Locations.FirstOrDefault(static l => l.IsInSource);
            pipelinePlan.AddDiagnostic(new DiagnosticInfo(
                DiagnosticDescriptors.HandlerShouldBeAsync,
                handlerLocation is not null ? LocationInfo.CreateFrom(handlerLocation) : null,
                [resolvedHandler.Value.HandlerMethodName]));
        }

        foreach (var contributor in executionPlanResult.Value.PipelineContributors)
        {
            contributor.ContributeToPipeline(new PipelineContributorContext(contributor.ContributorId, operationModel, pipelinePlan, operationLookup, resolvedHandler?.Value, tenantIdTypeName));
        }

        foreach (var unsatisfiedRequirement in pipelinePlan.VariableRegistry.GetUnsatisfiedRequirements())
        {
            pipelinePlan.AddDiagnostic(
                new DiagnosticInfo(
                    FoundationModuleDiagnosticDescriptors.MissingRequiredVariable,
                    (Location?)null,
                    [
                        unsatisfiedRequirement.Requirement.ConsumerId,
                        unsatisfiedRequirement.Requirement.Key.Purpose,
                        unsatisfiedRequirement.Requirement.Key.TypeName,
                        unsatisfiedRequirement.Requirement.RequiredBy.ToString(),
                        unsatisfiedRequirement.EarliestAvailableFrom?.ToString() ?? "not configured",
                    ]));
        }

        var diagnostics = pipelinePlan.Diagnostics;
        var hasErrors = false;
        foreach (var d in diagnostics)
        {
            if (d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            {
                hasErrors = true;
                break;
            }
        }

        if (diagnostics.Count == 0)
        {
            return Result.Success(pipelinePlan);
        }

        var diagnosticArray = new DiagnosticInfo[diagnostics.Count];
        for (var i = 0; i < diagnostics.Count; i++)
        {
            diagnosticArray[i] = diagnostics[i];
        }

        if (hasErrors)
        {
            return Result<PipelinePlan>.Failure(diagnosticArray);
        }

        return new Result<PipelinePlan>(pipelinePlan, new EquatableList<DiagnosticInfo>(diagnosticArray));
    }

    private static string NormalizeMetadataName(string metadataName)
    {
        return metadataName.StartsWith("global::", StringComparison.Ordinal)
            ? metadataName.Substring("global::".Length)
            : metadataName;
    }

    private static Result<OperationHandlerModel>? ResolveHandlerIfRequired(
        INamedTypeSymbol operationImplementationType,
        Compilation compilation,
        IReadOnlyList<IPipelineContributor> contributors,
        OperationHandlerModel? handler)
    {
        if (handler is not null)
        {
            return Result.Success(handler);
        }

        if (!contributors.Any(contributor => contributor is HandlerExecutionModule))
        {
            return null;
        }

        return HandlerDiscovery.FindHandler(operationImplementationType, compilation);
    }
}
