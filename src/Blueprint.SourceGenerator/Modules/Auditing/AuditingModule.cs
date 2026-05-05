using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that writes an audit record via <c>IAuditSink</c> after
/// successful handler execution. Auditing is best-effort: sink failures are caught and logged but
/// do not propagate as pipeline failures.
/// </summary>
/// <remarks>
/// <para>
/// This module runs at Order=950, which places it after <see cref="UnitOfWorkModule"/> (Order=900)
/// but before <see cref="BackgroundTaskFlushModule"/> (Order=1000). The rationale is that audit
/// records should capture committed state, and background tasks should only run after auditing.
/// </para>
/// <para>
/// <c>IAuditSink</c> implementations must persist independently of the ambient unit-of-work scope.
/// If the sink needs transactional guarantees, it should manage its own persistence boundary.
/// </para>
/// <para>
/// Operations decorated with <c>[DoNotAudit]</c> are excluded from audit recording regardless
/// of whether a sink is registered.
/// </para>
/// <para>
/// Generated audit events follow OpenTelemetry semantic conventions with the <c>blueprint.audit.*</c>
/// naming prefix. Events carry the operation type name and, on failure, standard exception tags
/// (<c>exception.type</c>, <c>exception.message</c>, <c>exception.stacktrace</c>) matching the
/// pattern established by <c>BlueprintActivitySource.RecordException</c>.
/// </para>
/// </remarks>
public sealed class AuditingModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(AuditingModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(AuditingModule);

    /// <inheritdoc />
    public int Order => 950;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        // Respect explicit opt-out via [DoNotAudit]
        if (HasDoNotAuditAttribute(operationType))
        {
            return;
        }

        var operationTypeName = context.Operation.TypeName;

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.PostExecution, ctx => RenderAuditSink(ctx, operationTypeName));
    }

    private static void RenderAuditSink(PipelineRenderContext context, string operationTypeName)
    {
        context.WriteBlankLine();

        context.If(
            "handlerResult.WasSuccessful",
            branch =>
            {
                branch.DeclareVariable(
                    "auditSink",
                    context.GetOptionalServiceExpression("Blueprint.Auditing.IAuditSink"));

                branch.If(
                    "auditSink != null",
                    innerBranch =>
                    {
                        // Emit structured audit started event with operation type tag
                        innerBranch.WriteStatement(
                            $"context.Activity?.AddEvent(new System.Diagnostics.ActivityEvent(\"blueprint.audit.started\", default, new System.Diagnostics.ActivityTagsCollection {{ {{ \"blueprint.operation.type\", \"{operationTypeName}\" }} }}));");

                        innerBranch.WriteStatement("try");
                        innerBranch.WriteStatement("{");
                        innerBranch.WriteStatement(
                            "    await auditSink.RecordAsync(context, handlerResult, System.Threading.CancellationToken.None);");

                        // Emit structured audit succeeded event with operation type tag
                        innerBranch.WriteStatement(
                            $"    context.Activity?.AddEvent(new System.Diagnostics.ActivityEvent(\"blueprint.audit.succeeded\", default, new System.Diagnostics.ActivityTagsCollection {{ {{ \"blueprint.operation.type\", \"{operationTypeName}\" }} }}));");

                        innerBranch.WriteStatement("}");
                        innerBranch.WriteStatement("catch (System.Exception auditException)");
                        innerBranch.WriteStatement("{");

                        // Emit structured audit failed event with exception details matching BlueprintActivitySource.RecordException schema
                        innerBranch.WriteStatement(
                            $"    context.Activity?.AddEvent(new System.Diagnostics.ActivityEvent(\"blueprint.audit.failed\", default, new System.Diagnostics.ActivityTagsCollection {{ {{ \"blueprint.operation.type\", \"{operationTypeName}\" }}, {{ \"exception.type\", auditException.GetType().FullName }}, {{ \"exception.message\", auditException.Message }}, {{ \"exception.stacktrace\", auditException.StackTrace }} }}));");

                        innerBranch.WriteStatement("}");
                    });
            });
    }

    private static bool HasDoNotAuditAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotAuditAttribute"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Auditing");
    }
}
