using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

internal static class PipelinePlanBuilder
{
    public static Result<PipelinePlan> Build(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult)
    {
        if (executionPlanResult.Value is null)
        {
            return Result<PipelinePlan>.Failure(executionPlanResult.Diagnostics);
        }

        var operationImplementationType = compilation.GetTypeByMetadataName(operationFullClassName);
        var operationModel = OperationModel.Create(operationImplementationType!);
        var pipelinePlan = new PipelinePlan(operationModel, new VariableRegistry());
        var operationLookup = new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlanResult.Value.PipelineContributors)
        {
            contributor.Contribute(new PipelineContributorContext(operationModel, pipelinePlan, operationLookup));
        }

        foreach (var unsatisfiedRequirement in pipelinePlan.VariableRegistry.GetUnsatisfiedRequirements(pipelinePlan.Contributions))
        {
            pipelinePlan.AddDiagnostic(
                new DiagnosticInfo(
                    FoundationModuleDiagnosticDescriptors.MissingRequiredVariable,
                    (Location?)null,
                    [
                        unsatisfiedRequirement.Requirement.ConsumerId,
                        unsatisfiedRequirement.Requirement.Key.Purpose,
                        unsatisfiedRequirement.Requirement.Key.TypeName,
                        unsatisfiedRequirement.RequiredBy?.ToString() ?? "unknown",
                        unsatisfiedRequirement.EarliestAvailableFrom?.ToString() ?? "not configured",
                    ]));
        }

        var failures = pipelinePlan.Diagnostics.ToArray();
        return failures.Length > 0 ? Result<PipelinePlan>.Failure(failures) : Result.Success(pipelinePlan);
    }
}
