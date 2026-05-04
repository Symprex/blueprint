using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that calls <c>IUnitOfWork.SaveChangesAsync</c> after
/// successful command handler execution. Only participates for operations implementing
/// <c>ICommand</c> (not queries or background tasks), and skips gracefully at runtime if no
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
        // Only participate for command operations — queries should not trigger persistence saves
        if (!IsCommand(context.Operation.TypeSymbol))
        {
            return;
        }

        // Respect explicit opt-out via [DoNotSaveChanges]
        if (HasDoNotSaveChangesAttribute(context.Operation.TypeSymbol))
        {
            return;
        }

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.PostExecution, RenderSaveChanges);
    }

    private static void RenderSaveChanges(PipelineRenderContext context)
    {
        context.WriteBlankLine();

        context.DeclareVariable(
            "unitOfWork",
            context.GetOptionalServiceExpression("Blueprint.Persistence.IUnitOfWork"));

        context.If(
            "unitOfWork != null && handlerResult.WasSuccessful",
            branch =>
            {
                branch.WriteStatement("await unitOfWork.SaveChangesAsync(context.OperationCancelled);");
            });
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "ICommand"
            && candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model");
    }

    private static bool HasDoNotSaveChangesAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotSaveChangesAttribute"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Persistence");
    }
}
