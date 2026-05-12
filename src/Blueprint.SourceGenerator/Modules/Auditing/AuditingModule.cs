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

        // Cache singleton IAuditSink at pipeline construction time (nullable — may not be registered)
        context.CacheService(
            "_auditSink",
            "Blueprint.Auditing.IAuditSink",
            "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Blueprint.Auditing.IAuditSink>(serviceProvider)",
            isNullable: true);

        var operationTypeName = context.Operation.TypeName;

        // Generate a safe field name from the operation type for the cached tags collection
        var safeFieldName = "_auditTags_" + operationTypeName.Replace(".", "_").Replace("+", "_");

        // Pre-declare a static readonly ActivityTagsCollection to avoid per-request allocations for constant tags
        context.AddDeferredClassMember(classBuilder =>
        {
            classBuilder.AddProperty(safeFieldName, Accessibility.Private)
                .SetType("System.Diagnostics.ActivityTagsCollection")
                .MakeStatic()
                .WithReadonlyValue(
                    $"new System.Diagnostics.ActivityTagsCollection {{ {{ \"blueprint.operation.type\", \"{operationTypeName}\" }} }}");
        });

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddSuccessGuardedRenderStep(PipelineStage.PostExecution, ctx => RenderAuditSink(ctx, operationTypeName, safeFieldName));
    }

    private static void RenderAuditSink(PipelineRenderContext context, string operationTypeName, string cachedTagsFieldName)
    {
        context.WriteBlankLine();

        // Delegate to shared helper — avoids duplicating the null-check, try/catch, and activity
        // event recording across every auditable pipeline.
        context.WriteStatement(
            $"await Blueprint.Runtime.Exec.PipelineHelpers.ExecuteAuditSinkAsync(this._auditSink, context, handlerResult, {cachedTagsFieldName}, \"{operationTypeName}\").ConfigureAwait(false);");
    }

    private static bool HasDoNotAuditAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotAuditAttribute"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Auditing");
    }
}
