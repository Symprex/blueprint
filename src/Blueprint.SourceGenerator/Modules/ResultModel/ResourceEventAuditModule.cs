using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that records a resource-event audit entry via
/// <c>IResourceEventAuditSink</c> after successful handler execution returns a
/// <c>ResourceEventResult</c> or <c>KeyOnlyResourceEventResult</c>.
/// </summary>
/// <remarks>
/// <para>
/// This module only activates for operations whose declared result contract includes a
/// resource-event result type. It runs at Order=960, placing it after general auditing
/// (Order=950) and after <see cref="UnitOfWorkModule"/> (Order=900), but before
/// <see cref="BackgroundTaskFlushModule"/> (Order=1000).
/// </para>
/// <para>
/// Resource-event auditing is best-effort: sink failures are caught and emitted as
/// <c>blueprint.resource_event_audit.failed</c> activity events but do not propagate
/// as pipeline failures.
/// </para>
/// <para>
/// The contract is save-only by design. There is no read-back, no ChangedValues, and no
/// GetCurrentDataAsync. The sink receives the stable event identity and resource identity.
/// </para>
/// </remarks>
public sealed class ResourceEventAuditModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(ResourceEventAuditModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ResourceEventAuditModule);

    /// <inheritdoc />
    public int Order => 960;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var castTypeName = GetResourceEventCastType(context.Operation.TypeSymbol);
        if (castTypeName is null)
        {
            return;
        }

        var operationTypeName = context.Operation.TypeName;

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddSuccessGuardedRenderStep(PipelineStage.PostExecution, ctx => RenderResourceEventAudit(ctx, operationTypeName));
    }

    private static void RenderResourceEventAudit(PipelineRenderContext context, string operationTypeName)
    {
        context.WriteBlankLine();

        // Delegate to shared helper — avoids duplicating the pattern match, null check,
        // try/catch, and activity event recording across every resource-event pipeline.
        context.WriteStatement(
            $"await Blueprint.Runtime.Exec.PipelineHelpers.ExecuteResourceEventAuditSinkAsync(this.ResourceEventAuditSink, context, handlerResult, \"{operationTypeName}\").ConfigureAwait(false);");
    }

    /// <summary>
    /// Determines the fully-qualified cast type name for pattern matching the handler result.
    /// Returns null if the operation does not declare a resource-event result type.
    /// <c>ResourceEventResult</c>, <c>KeyOnlyResourceEventResult</c>, and <c>ResourceEvent</c>
    /// are all supported resource-event result hierarchies that require audit sink execution.
    /// </summary>
    private static string? GetResourceEventCastType(INamedTypeSymbol operationType)
    {
        var declaredResultType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (declaredResultType is null)
        {
            return null;
        }

        for (var current = declaredResultType; current is not null; current = current.BaseType)
        {
            if (current.IsInNamespace("Blueprint", "Runtime", "Exec"))
            {
                if (current.Name == "ResourceEventResult")
                {
                    return "Blueprint.Runtime.Exec.ResourceEventResult";
                }

                if (current.Name == "KeyOnlyResourceEventResult")
                {
                    return "Blueprint.Runtime.Exec.KeyOnlyResourceEventResult";
                }

                // DeferredResourceEventResult<T> is mutated in-place (ResolvedContent populated),
                // so the audit module still needs to activate and extract event info from it
                if (current.Name == "DeferredResourceEventResult")
                {
                    return "Blueprint.Runtime.Exec.IDeferredResourceEventResult";
                }
            }

            if (current.IsInNamespace("Blueprint", "Http")
                && current.Name == "ResourceEvent")
            {
                return "Blueprint.Http.ResourceEvent";
            }
        }

        return null;
    }
}
