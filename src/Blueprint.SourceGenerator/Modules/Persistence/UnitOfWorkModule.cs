using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that calls <c>IUnitOfWork.SaveChangesAsync</c> after
/// successful handler execution. Participates for operations implementing <c>ICommand</c> or
/// <c>IBackgroundTask</c> (not queries), and skips gracefully at runtime if no
/// <c>IUnitOfWork</c> is registered in DI.
/// </summary>
/// <remarks>
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
        var isCommand = IsCommand(operationType);
        var isBackgroundTask = IsBackgroundTask(operationType);

        // Emit BPP001 when [DoNotSaveChanges] is applied to a non-command where it has no effect
        if (!isCommand && !isBackgroundTask && HasDoNotSaveChangesAttribute(operationType))
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    PersistenceDiagnosticDescriptors.DoNotSaveChangesOnNonCommand,
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));

            return;
        }

        if (!SavesUnitOfWork(operationType))
        {
            return;
        }

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddSuccessGuardedRenderStep(PipelineStage.PostExecution, RenderSaveChanges);
    }

    /// <summary>
    /// Whether the pipeline generated for <paramref name="operationType"/> saves the unit of work: a command or background
    /// task without <c>[DoNotSaveChanges]</c>. Queries never trigger a persistence save.
    /// </summary>
    /// <remarks>
    /// The one definition of that condition, shared with modules that must know whether a save will follow — the Rebus
    /// transaction enlists its outbox only where one does.
    /// </remarks>
    /// <param name="operationType">The operation type.</param>
    /// <returns><see langword="true"/> when the generated pipeline contains the unit of work save.</returns>
    internal static bool SavesUnitOfWork(INamedTypeSymbol operationType)
    {
        return (IsCommand(operationType) || IsBackgroundTask(operationType))
               && !HasDoNotSaveChangesAttribute(operationType);
    }

    private static void RenderSaveChanges(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("if (this.HasUnitOfWork)");
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
