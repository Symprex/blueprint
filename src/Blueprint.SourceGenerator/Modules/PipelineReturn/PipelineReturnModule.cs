using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Emits the final <c>return handlerResult</c> statement in the Cleanup stage, allowing PostExecution
/// contributors to mutate the result between handler execution and the method exit point.
/// </summary>
public sealed class PipelineReturnModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(PipelineReturnModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(PipelineReturnModule);

    /// <inheritdoc />
    public int Order => int.MaxValue;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.Cleanup);

        context.AddRenderStep(PipelineStage.Cleanup, RenderReturn);
    }

    private static void RenderReturn(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.Return(PipelineCodeGenerator.WrapOperationResult("handlerResult", context.IsPipelineAsync()));
    }
}
