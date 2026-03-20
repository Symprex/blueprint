using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

internal static class PipelinePlanBuilder
{
    public static Result<PipelinePlan> Build(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult,
        OperationHandlerModel? handler = null)
    {
        if (executionPlanResult.Value is null)
        {
            return Result<PipelinePlan>.Failure(executionPlanResult.Diagnostics);
        }

        var operationImplementationType = compilation.GetTypeByMetadataName(NormalizeMetadataName(operationFullClassName));
        var operationModel = OperationModel.Create(operationImplementationType!);
        var pipelinePlan = new PipelinePlan(operationModel, new VariableRegistry());
        var operationLookup = new CompilationOperationLookup(compilation);
        var resolvedHandler = ResolveHandlerIfRequired(
            operationImplementationType!,
            compilation,
            executionPlanResult.Value.PipelineContributors,
            handler);

        if (resolvedHandler is { Value: null })
        {
            return Result<PipelinePlan>.Failure(resolvedHandler.Diagnostics);
        }

        foreach (var contributor in executionPlanResult.Value.PipelineContributors)
        {
            contributor.ContributeToPipeline(new PipelineContributorContext(contributor.ContributorId, operationModel, pipelinePlan, operationLookup, resolvedHandler?.Value));
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

        var failures = pipelinePlan.Diagnostics.ToArray();
        return failures.Length > 0 ? Result<PipelinePlan>.Failure(failures) : Result.Success(pipelinePlan);
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
