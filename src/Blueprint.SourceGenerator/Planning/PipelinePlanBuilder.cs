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

        foreach (var requirement in pipelinePlan.VariableRegistry.GetUnsatisfiedRequirements())
        {
            pipelinePlan.AddDiagnostic(
                new DiagnosticInfo(
                    FoundationModuleDiagnosticDescriptors.MissingRequiredVariable,
                    (Location?)null,
                    [
                        requirement.ConsumerId,
                        requirement.Key.Purpose,
                        requirement.Key.TypeName,
                    ]));
        }

        var failures = pipelinePlan.Diagnostics.ToArray();
        return failures.Length > 0 ? Result<PipelinePlan>.Failure(failures) : Result.Success(pipelinePlan);
    }
}
