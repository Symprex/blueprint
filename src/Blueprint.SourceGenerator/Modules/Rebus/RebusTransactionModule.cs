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
/// <item><description>
/// A <c>using var rebusScope = new RebusTransactionScope();</c> as the last step of the Validation stage: after the tenant
/// is resolved and the operation authorised and validated, and before the handler, where legacy's
/// <c>StartRebusTransactionScopeMiddleware</c> opened it. So a message sent while the tenant is resolved, a property
/// populated or the operation authorised is sent at once, as legacy sent it, rather than held in the scope and dropped
/// when validation or authorisation then refuses the operation
/// </description></item>
/// <item><description>
/// In a pipeline that saves a unit of work and when Blueprint.Rebus is referenced, straight after the scope opens and in
/// the same render step, so nothing can come between them, a call to
/// <c>Blueprint.Rebus.RebusTransactionEnlistment.EnlistAsync</c> with the scope's transaction, guarded by an
/// <c>IUnitOfWork</c> being registered, as legacy enlisted, before the handler sends anything
/// </description></item>
/// <item><description>An <c>await rebusScope.CompleteAsync();</c> in the PostExecution stage after handler success</description></item>
/// </list>
/// </para>
/// <para>
/// Nothing that runs before the Validation stage's end uses the scope: no other module renders a reference to it, and the
/// only thing that could depend on it being ambient earlier is a send from one of those steps, which legacy sent outside
/// any scope.
/// </para>
/// <para>
/// Commands (<c>ICommand</c>) and background tasks (<c>Blueprint.Tasks.IBackgroundTask</c>) participate; queries do
/// not. Operations decorated with <c>[NoRebusTransaction]</c> (or the full name
/// <c>Blueprint.Rebus.NoRebusTransactionAttribute</c>) are excluded from transaction wrapping.
/// </para>
/// <para>
/// A pipeline that runs while another Rebus transaction is ambient — a background task executed from inside a message
/// handler, for example — opens a scope of its own: Rebus nests the scope and restores the outer one when it is
/// disposed, so the inner operation's messages are sent when it completes, not when the outer transaction does.
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
    private const string RebusTransactionEnlistmentType = "Blueprint.Rebus.RebusTransactionEnlistment";

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

        // Queries do not publish messages — skip the transaction scope to avoid allocation overhead.
        // Commands and background tasks that publish messages through Rebus need the scope to batch and commit,
        // as legacy Blueprint gave both.
        if (!IsCommand(context.Operation.TypeSymbol) && !IsBackgroundTask(context.Operation.TypeSymbol))
        {
            return;
        }

        // Cache a boolean field set at pipeline construction time. When no Rebus IBus is
        // registered (e.g. integration tests without messaging, or apps that reference
        // Blueprint.Rebus but only use it in a subset of hosts), the generated pipeline
        // skips the RebusTransactionScope entirely — avoiding per-request AsyncLocal writes
        // and TransactionContext allocation.
        context.CacheService(
            "_hasRebus",
            "bool",
            "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Rebus.Bus.IBus>(serviceProvider) != null");

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();

        // Enlist the consumer's outbox only where a unit of work save follows, and only when Blueprint.Rebus (which
        // defines the seam) is referenced: a consumer of Rebus alone must still compile.
        var enlist = UnitOfWorkModule.SavesUnitOfWork(context.Operation.TypeSymbol)
                     && context.FindTypeByMetadataName(RebusTransactionEnlistmentType) is not null;

        // Legacy opened the scope and enlisted from Execution-stage middleware added before the handler, so after the tenant
        // was set, and the operation authorised and validated: an enlistment resolving a tenant-scoped session any earlier
        // gets an untenanted one, which the handler then shares, and a message sent from those steps went out at once. The
        // last Validation step is the same point here, as this module's own Execution step would render after the handler
        // it depends on. One step for both, because two steps in one stage have no guaranteed order.
        context.AddRenderStep(
            PipelineStage.Validation,
            enlist ? RenderTransactionScopeStartAndEnlistment : RenderTransactionScopeStart);

        // PostExecution: complete the transaction on success
        context.AddSuccessGuardedRenderStep(PipelineStage.PostExecution, RenderTransactionScopeComplete);
    }

    private static void RenderTransactionScopeStart(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement($"using var rebusTransactionScope = this._hasRebus ? new global::{RebusTransactionScopeType}() : null;");
    }

    private static void RenderTransactionScopeStartAndEnlistment(PipelineRenderContext context)
    {
        RenderTransactionScopeStart(context);

        // Before the handler sends anything; and only with an IUnitOfWork registered, so only where the save that writes
        // an enlisted outbox will actually run.
        context.WriteStatement("if (rebusTransactionScope != null && this.HasUnitOfWork)");
        context.WriteStatement("{");
        context.WriteStatement(
            $"    await global::{RebusTransactionEnlistmentType}.EnlistAsync(rebusTransactionScope.TransactionContext, context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);");
        context.WriteStatement("}");
    }

    private static void RenderTransactionScopeComplete(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("if (rebusTransactionScope != null) await rebusTransactionScope.CompleteAsync().ConfigureAwait(false);");
    }

    private static bool HasNoRebusTransactionAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is NoRebusTransactionAttributeName
            && attr.AttributeClass.IsInNamespace("Blueprint", "Rebus"));
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "ICommand"
            && candidate.IsInNamespace("Blueprint", "Model"));
    }

    private static bool IsBackgroundTask(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "IBackgroundTask"
            && candidate.IsInNamespace("Blueprint", "Tasks"));
    }
}
