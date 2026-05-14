using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a Rebus <c>RebusTransactionScope</c> that wraps handler execution, ensuring
/// messages published during the pipeline are batched and only sent after successful completion.
/// </summary>
/// <remarks>
/// <para>
/// This module generates:
/// <list type="bullet">
/// <item><description>A <c>using var rebusScope = new RebusTransactionScope();</c> in the Setup stage</description></item>
/// <item><description>An <c>await rebusScope.CompleteAsync();</c> in the PostExecution stage after handler success</description></item>
/// </list>
/// </para>
/// <para>
/// Operations decorated with <c>[NoRebusTransaction]</c> (or the full name
/// <c>Blueprint.Rebus.NoRebusTransactionAttribute</c>) are excluded from transaction wrapping.
/// </para>
/// <para>
/// This module is included in the default pipeline configuration and auto-detects Rebus
/// availability by checking for <c>Rebus.Transport.RebusTransactionScope</c> in the
/// compilation. When Rebus is not referenced, the module silently no-ops. This eliminates
/// the need for consumer companion generators to manually compose the module.
/// </para>
/// </remarks>
public sealed class RebusTransactionModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string RebusTransactionScopeType = "Rebus.Transport.RebusTransactionScope";
    private const string NoRebusTransactionAttributeName = "NoRebusTransactionAttribute";

    /// <inheritdoc />
    public string ModuleId => nameof(RebusTransactionModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

    /// <inheritdoc />
    public string ContributorId => nameof(RebusTransactionModule);

    /// <inheritdoc />
    public int Order => 50;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        // Auto-detect: only activate when Rebus is referenced in the compilation
        if (context.FindTypeByMetadataName(RebusTransactionScopeType) is null)
        {
            return;
        }

        // Skip operations that have opted out of Rebus transaction wrapping
        if (HasNoRebusTransactionAttribute(context.Operation.TypeSymbol))
        {
            return;
        }

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();

        // Setup: create the transaction scope (using ensures dispose on method exit)
        context.AddRenderStep(PipelineStage.Setup, RenderTransactionScopeStart);

        // PostExecution: complete the transaction on success
        context.AddSuccessGuardedRenderStep(PipelineStage.PostExecution, RenderTransactionScopeComplete);
    }

    private static void RenderTransactionScopeStart(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement($"using var rebusTransactionScope = new global::{RebusTransactionScopeType}();");
    }

    private static void RenderTransactionScopeComplete(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("await rebusTransactionScope.CompleteAsync().ConfigureAwait(false);");
    }

    private static bool HasNoRebusTransactionAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is NoRebusTransactionAttributeName
            && attr.AttributeClass.IsInNamespace("Blueprint", "Rebus"));
    }
}
