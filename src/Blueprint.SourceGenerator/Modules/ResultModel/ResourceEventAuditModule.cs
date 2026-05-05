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
        context.AddRenderStep(PipelineStage.PostExecution, ctx => RenderResourceEventAudit(ctx, operationTypeName, castTypeName));
    }

    private static void RenderResourceEventAudit(PipelineRenderContext context, string operationTypeName, string castTypeName)
    {
        context.WriteBlankLine();

        context.If(
            $"handlerResult.WasSuccessful && handlerResult is {castTypeName} _reAuditResult",
            branch =>
            {
                branch.DeclareVariable(
                    "resourceEventAuditSink",
                    context.GetOptionalServiceExpression("Blueprint.Auditing.IResourceEventAuditSink"));

                branch.If(
                    "resourceEventAuditSink != null",
                    innerBranch =>
                    {
                        innerBranch.WriteStatement(
                            $"context.Activity?.AddEvent(new System.Diagnostics.ActivityEvent(\"blueprint.resource_event_audit.started\", default, new System.Diagnostics.ActivityTagsCollection {{ {{ \"blueprint.operation.type\", \"{operationTypeName}\" }}, {{ \"blueprint.resource_event.id\", _reAuditResult.EventId }}, {{ \"blueprint.resource_event.resource_id\", _reAuditResult.ResourceId }} }}));");

                        innerBranch.WriteStatement("try");
                        innerBranch.WriteStatement("{");
                        innerBranch.WriteStatement(
                            "    await resourceEventAuditSink.RecordAsync(_reAuditResult.EventId, _reAuditResult.ResourceId, context, System.Threading.CancellationToken.None);");

                        innerBranch.WriteStatement(
                            $"    context.Activity?.AddEvent(new System.Diagnostics.ActivityEvent(\"blueprint.resource_event_audit.succeeded\", default, new System.Diagnostics.ActivityTagsCollection {{ {{ \"blueprint.operation.type\", \"{operationTypeName}\" }}, {{ \"blueprint.resource_event.id\", _reAuditResult.EventId }} }}));");

                        innerBranch.WriteStatement("}");
                        innerBranch.WriteStatement("catch (System.Exception resourceEventAuditException)");
                        innerBranch.WriteStatement("{");

                        innerBranch.WriteStatement(
                            $"    context.Activity?.AddEvent(new System.Diagnostics.ActivityEvent(\"blueprint.resource_event_audit.failed\", default, new System.Diagnostics.ActivityTagsCollection {{ {{ \"blueprint.operation.type\", \"{operationTypeName}\" }}, {{ \"blueprint.resource_event.id\", _reAuditResult.EventId }}, {{ \"exception.type\", resourceEventAuditException.GetType().FullName }}, {{ \"exception.message\", resourceEventAuditException.Message }}, {{ \"exception.stacktrace\", resourceEventAuditException.StackTrace }} }}));");

                        innerBranch.WriteStatement("}");
                    });
            });
    }

    /// <summary>
    /// Determines the fully-qualified cast type name for pattern matching the handler result.
    /// Returns null if the operation does not declare a resource-event result type.
    /// <c>ResourceEventResult</c> and <c>KeyOnlyResourceEventResult</c> are sibling types
    /// (both inherit from <c>OperationResult</c>) so the correct one must be selected at compile time.
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
            if (current.ContainingNamespace?.ToDisplayString() == "Blueprint.Runtime.Exec")
            {
                if (current.Name == "ResourceEventResult")
                {
                    return "Blueprint.Runtime.Exec.ResourceEventResult";
                }

                if (current.Name == "KeyOnlyResourceEventResult")
                {
                    return "Blueprint.Runtime.Exec.KeyOnlyResourceEventResult";
                }
            }
        }

        return null;
    }
}
