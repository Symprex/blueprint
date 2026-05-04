using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that flushes any background tasks enqueued during handler execution.
/// Only flushes when the handler result represents success (not a validation failure or unhandled exception).
/// The flush is skipped gracefully at runtime if no <c>IBackgroundTaskScheduler</c> is registered in DI.
/// </summary>
public sealed class BackgroundTaskFlushModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(BackgroundTaskFlushModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(BackgroundTaskFlushModule);

    /// <inheritdoc />
    public int Order => 1000;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.PostExecution, RenderTaskFlush);
    }

    private static void RenderTaskFlush(PipelineRenderContext context)
    {
        context.WriteBlankLine();

        context.If(
            "handlerResult.WasSuccessful",
            branch =>
            {
                branch.DeclareVariable(
                    "backgroundTaskScheduler",
                    context.GetOptionalServiceExpression("Blueprint.Tasks.IBackgroundTaskScheduler"));

                branch.If(
                    "backgroundTaskScheduler != null",
                    innerBranch =>
                    {
                        innerBranch.WriteStatement("await backgroundTaskScheduler.FlushAsync(context.OperationCancelled);");
                    });
            });
    }
}
