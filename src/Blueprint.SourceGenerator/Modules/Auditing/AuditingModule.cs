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

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.PostExecution, RenderAuditSink);
    }

    private static void RenderAuditSink(PipelineRenderContext context)
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
                        innerBranch.WriteStatement("try");
                        innerBranch.WriteStatement("{");
                        innerBranch.WriteStatement(
                            "    await auditSink.RecordAsync(context, handlerResult, System.Threading.CancellationToken.None);");
                        innerBranch.WriteStatement("}");
                        innerBranch.WriteStatement("catch (System.Exception auditException)");
                        innerBranch.WriteStatement("{");
                        innerBranch.WriteStatement(
                            "    context.Activity?.AddEvent(new System.Diagnostics.ActivityEvent(\"audit.sink.failed\", default, new System.Diagnostics.ActivityTagsCollection { { \"exception.message\", auditException.Message } }));");
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
