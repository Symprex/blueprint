using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Observability;

/// <summary>
/// Detects <c>[DoNotRecordPerformanceMetrics]</c> on operation types and sets
/// <see cref="PipelinePlan.SuppressActivity"/> so the generated pipeline suppresses
/// the Activity/trace span in <c>OperationExecutorShell</c>.
/// </summary>
/// <remarks>
/// This module runs at Order=0, before all other contributors, because suppressing
/// activity is an observability concern that must be decided before any stage emits
/// trace data. The module has no render steps and does not participate in code generation
/// directly; it only sets a plan flag that <c>PipelineCodeGenerator</c> reads.
/// It also contributes <c>observability.activityEnabled</c> to the per-operation debug manifest.
/// </remarks>
internal sealed class PerformanceMetricsModule : IBlueprintSourceGeneratorModule, IPipelineContributor, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(PerformanceMetricsModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(PerformanceMetricsModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        if (HasDoNotRecordPerformanceMetricsAttribute(context.Operation.TypeSymbol))
        {
            context.SuppressActivity();
        }
    }

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var suppressed = HasDoNotRecordPerformanceMetricsAttribute(context.GetOperationTypeSymbol());
        context.AddEntry("observability.activityEnabled", (!suppressed).ToString());
    }

    private static bool HasDoNotRecordPerformanceMetricsAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotRecordPerformanceMetricsAttribute"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Auditing");
    }
}
