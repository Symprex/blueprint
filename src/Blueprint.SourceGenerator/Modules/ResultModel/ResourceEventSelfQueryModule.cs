using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that, for a command, resolves <c>ResourceEvent.SelfQuery</c> by
/// executing the query through the generated pipeline when the handler returns a
/// <c>ResourceEvent</c> with <c>Data == null</c> and <c>SelfQuery != null</c>, or a
/// <c>DeferredResourceEventResult&lt;T&gt;</c> that carries a self-query for lazy resolution. A query's self query is
/// not run, as legacy ran none; its pipeline only marks data it hands on as its own to expand.
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

        // Legacy ran an event's self query in ResourceEventHandler (Signature365 8b40ecc961, ResourceEventHandler.cs:48-52),
        // whose middleware matched commands only (ResourceEventHandlerMiddlewareBuilder.cs:12-15, `operation.IsCommand`). A
        // query's event keeps the data its handler gave it, but data it hands on is still its own to expand, as legacy's
        // ResourceExpandMiddleware matched every operation.
        if (!IsCommand(context.Operation.TypeSymbol))
        {
            context.AddRenderStep(PipelineStage.PostExecution, RenderHandedOnDataMark);
            return;
        }

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.PostExecution, ctx => RenderSelfQueryResolution(ctx, operationTypeName));
    }

    private static void RenderSelfQueryResolution(PipelineRenderContext context, string operationTypeName)
    {
        context.WriteBlankLine();
        context.WriteStatement(
            $"await ResolveResourceEventSelfQueryAsync(this.Executor, context, handlerResult, this.Logger, \"{operationTypeName}\").ConfigureAwait(false);");
    }

    private static void RenderHandedOnDataMark(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("MarkHandedOnResourceEventData(handlerResult);");
    }

    /// <summary>
    /// Whether the operation is a command, the only kind whose event's self query legacy ran: its
    /// <c>ResourceEventHandlerMiddlewareBuilder</c> matched <c>operation.IsCommand</c> (Signature365 <c>8b40ecc961</c>,
    /// <c>ResourceEventHandlerMiddlewareBuilder.cs:12-15</c>), which is <c>typeof(ICommand).IsAssignableFrom(OperationType)</c>
    /// (<c>ApiOperationDescriptor.cs:124</c>). A background task is not one.
    /// </summary>
    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "ICommand"
            && candidate.IsInNamespace("Blueprint", "Model"));
    }

    /// <summary>
    /// Determines whether the operation's declared result type includes a self-query-bearing result.
    /// Both legacy <c>ResourceEvent</c> (from <c>Blueprint.Http</c>) and new-style
    /// <c>DeferredResourceEventResult&lt;T&gt;</c> are detected, as is a <c>Results&lt;T1, T2&gt;</c> with such a member: legacy
    /// resolved the self query of the event its command returned, whatever the command declared (Signature365
    /// <c>8b40ecc961</c>, <c>ResourceEventHandler.cs:26-30</c>, <c>:49-52</c>).
    /// </summary>
    private static bool HasSelfQueryResult(INamedTypeSymbol operationType)
    {
        var declaredResultType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (declaredResultType is null)
        {
            return false;
        }

        return OperationResultContractHelper.TryGetResultsUnionMembers(declaredResultType, out var unionMembers)
            ? unionMembers.Any(IsSelfQueryResult)
            : IsSelfQueryResult(declaredResultType);
    }

    private static bool IsSelfQueryResult(ITypeSymbol declaredResultType)
    {
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
