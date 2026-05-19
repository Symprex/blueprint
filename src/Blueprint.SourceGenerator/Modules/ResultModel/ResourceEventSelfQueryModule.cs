using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that resolves <c>ResourceEvent.SelfQuery</c> by
/// executing the query through the generated pipeline when the handler returns a
/// <c>ResourceEvent</c> with <c>Data == null</c> and <c>SelfQuery != null</c>, or a
/// <c>DeferredResourceEventResult&lt;T&gt;</c> that carries a self-query for lazy resolution.
/// </summary>
/// <remarks>
/// <para>
/// This module runs at Order=-5, placing it before <see cref="LinkGenerationModule"/> (Order=0)
/// so that the resolved Data is available for HATEOAS self-link generation, and before all
/// success-guarded PostExecution steps (auditing, persistence, background tasks) so those
/// modules see the enriched event.
/// </para>
/// <para>
/// The module also stamps <c>CorrelationId</c> and <c>Operation</c> on the <c>ResourceEvent</c>
/// before resolution, replicating S365's <c>ResourceEventHandler</c> middleware enrichment.
/// </para>
/// <para>
/// Resolution is best-effort: if the SelfQuery's operation is unregistered or the nested
/// execution fails, Data remains null and a warning is logged. The outer pipeline still succeeds.
/// A recursion guard prevents infinite loops when nested queries inadvertently trigger their own
/// SelfQuery resolution.
/// </para>
/// </remarks>
public sealed class ResourceEventSelfQueryModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(ResourceEventSelfQueryModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ResourceEventSelfQueryModule);

    /// <inheritdoc />
    public int Order => -5;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        if (!HasSelfQueryResult(context.Operation.TypeSymbol))
        {
            return;
        }

        var operationTypeName = context.Operation.TypeName;

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.PostExecution, ctx => RenderSelfQueryResolution(ctx, operationTypeName));
    }

    private static void RenderSelfQueryResolution(PipelineRenderContext context, string operationTypeName)
    {
        context.WriteBlankLine();
        context.WriteStatement(
            $"await Blueprint.Runtime.Exec.PipelineHelpers.ResolveResourceEventSelfQueryAsync(this.Executor, context, handlerResult, this.Logger, \"{operationTypeName}\").ConfigureAwait(false);");
    }

    /// <summary>
    /// Determines whether the operation's declared result type includes a self-query-bearing result.
    /// Both legacy <c>ResourceEvent</c> (from <c>Blueprint.Http</c>) and new-style
    /// <c>DeferredResourceEventResult&lt;T&gt;</c> are detected.
    /// </summary>
    private static bool HasSelfQueryResult(INamedTypeSymbol operationType)
    {
        var declaredResultType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (declaredResultType is null)
        {
            return false;
        }

        for (var current = declaredResultType; current is not null; current = current.BaseType)
        {
            // Legacy ResourceEvent from Blueprint.Http
            if (current.IsInNamespace("Blueprint", "Http") && current.Name == "ResourceEvent")
            {
                return true;
            }

            // New-style DeferredResourceEventResult<T> from Blueprint.Runtime.Exec
            if (current.IsInNamespace("Blueprint", "Runtime", "Exec") && current.Name == "DeferredResourceEventResult")
            {
                return true;
            }
        }

        return false;
    }
}
