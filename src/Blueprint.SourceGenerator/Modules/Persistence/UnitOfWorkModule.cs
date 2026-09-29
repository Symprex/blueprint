using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that calls <c>IUnitOfWork.SaveChangesAsync</c> after
/// successful handler execution. Participates for every operation without <c>[DoNotSaveChanges]</c> — commands,
/// background tasks and queries alike — and skips gracefully at runtime if no <c>IUnitOfWork</c> is registered in DI.
/// </summary>
/// <remarks>
/// <para>
/// This is legacy's rule: Signature365's <c>SaveMartenChangesMiddleware</c> matched any operation whose type had no
/// <c>[DoNotSaveChanges]</c> (Signature365 <c>8b40ecc961</c>, <c>Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29</c>),
/// so a query's pipeline saved its session too (Portal <c>Microsoft365AddOnConnectQueryExecutorPipeline.cs:133-134</c>).
/// Legacy left the save out of a nested execution (<c>SupportsNestedExecution =&gt; false</c>), which this framework
/// makes only for a resource event's self query, sharing its command's session. So a query's save, and that of any
/// operation that is neither a command nor a background task (<see cref="IsSkippedWhenNested"/>), is skipped when the
/// context is nested. A command's and a background task's save runs nested or not, as it always has here.
/// </para>
/// <para>
/// Disposal of the unit-of-work instance is owned by the DI scope, not by this module.
/// Implementations should register as scoped services so the container manages their lifetime.
/// </para>
/// <para>
/// This module orders before <see cref="BackgroundTaskFlushModule"/> to ensure that persistence
/// is committed before background tasks are dispatched, preserving read-your-writes semantics
/// for any tasks that reference the committed data.
/// </para>
/// </remarks>
public sealed class UnitOfWorkModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(UnitOfWorkModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(UnitOfWorkModule);

    /// <inheritdoc />
    public int Order => 900;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        if (!SavesUnitOfWork(operationType))
        {
            return;
        }

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddSuccessGuardedRenderStep(
            PipelineStage.PostExecution,
            IsSkippedWhenNested(operationType) ? RenderSaveChangesUnlessNested : RenderSaveChanges);
    }

    /// <summary>
    /// Whether the pipeline generated for <paramref name="operationType"/> saves the unit of work: any operation without
    /// <c>[DoNotSaveChanges]</c>, as legacy's <c>SaveMartenChangesMiddleware</c> matched (Signature365 <c>8b40ecc961</c>,
    /// <c>Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29</c>).
    /// </summary>
    /// <remarks>
    /// The one definition of that condition, shared with modules that must know whether a save will follow — the Rebus
    /// transaction enlists its outbox only where one does, and the debug manifest reports it.
    /// </remarks>
    /// <param name="operationType">The operation type.</param>
    /// <returns><see langword="true"/> when the generated pipeline contains the unit of work save.</returns>
    internal static bool SavesUnitOfWork(INamedTypeSymbol operationType)
    {
        return !HasDoNotSaveChangesAttribute(operationType);
    }

    /// <summary>
    /// Whether the unit of work save, and the Rebus transaction scope and enlistment, are left out of a nested execution of
    /// the pipeline generated for <paramref name="operationType"/>: any operation that is neither a command nor a
    /// background task.
    /// </summary>
    /// <remarks>
    /// Legacy left all of them out of every nested execution (<c>SupportsNestedExecution =&gt; false</c> on
    /// <c>SaveMartenChangesMiddleware</c>, <c>UseMartenAsOutboxMiddleware</c> and the Rebus scope middlewares;
    /// <c>Blueprint/CodeGennedExecutor.cs:99-100</c>). The one nested execution this framework makes is a resource event's
    /// self query, which shares its command's scope and session, so a query must not open a scope of its own, enlist the
    /// session again or save it there. Commands and background tasks took part before queries did, nested or not, and
    /// still do: their nested behaviour is not changed.
    /// </remarks>
    /// <param name="operationType">The operation type.</param>
    /// <returns><see langword="true"/> when the generated code checks <c>context.IsNested</c> first.</returns>
    internal static bool IsSkippedWhenNested(INamedTypeSymbol operationType)
    {
        return !IsCommand(operationType) && !IsBackgroundTask(operationType);
    }

    private static void RenderSaveChanges(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("if (this.HasUnitOfWork)");
        context.WriteStatement("{");
        context.WriteStatement("    await SaveUnitOfWorkAsync(context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);");
        context.WriteStatement("}");
    }

    private static void RenderSaveChangesUnlessNested(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("if (this.HasUnitOfWork && !context.IsNested)");
        context.WriteStatement("{");
        context.WriteStatement("    await SaveUnitOfWorkAsync(context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);");
        context.WriteStatement("}");
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

    private static bool HasDoNotSaveChangesAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotSaveChangesAttribute"
            && attr.AttributeClass.IsInNamespace("Blueprint", "Persistence"));
    }
}
