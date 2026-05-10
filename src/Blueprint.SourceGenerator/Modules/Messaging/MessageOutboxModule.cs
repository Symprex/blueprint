using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that calls <c>IMessageOutbox.CommitAsync</c> after
/// successful command handler execution. Only participates for operations implementing
/// <c>ICommand</c> (not queries or background tasks), and skips gracefully at runtime if no
/// <c>IMessageOutbox</c> is registered in DI.
/// </summary>
/// <remarks>
/// <para>
/// This module mirrors the legacy Blueprint Rebus transaction-scope middleware pattern:
/// messages enqueued by the handler during execution are committed only when the handler
/// succeeds. On failure the DI scope disposes the outbox, discarding pending messages.
/// </para>
/// <para>
/// The module orders after <see cref="UnitOfWorkModule"/> (persistence) but before
/// <see cref="BackgroundTaskFlushModule"/> (task dispatch), ensuring that persistence is
/// committed before messages are published, and messages are published before background
/// tasks are flushed.
/// </para>
/// </remarks>
public sealed class MessageOutboxModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(MessageOutboxModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(MessageOutboxModule);

    /// <inheritdoc />
    public int Order => 920;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;
        var isCommand = IsCommand(operationType);

        if (!isCommand && HasDoNotPublishMessagesAttribute(operationType))
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    MessagingDiagnosticDescriptors.DoNotPublishMessagesOnNonCommand,
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));

            return;
        }

        if (!isCommand)
        {
            return;
        }

        if (HasDoNotPublishMessagesAttribute(operationType))
        {
            return;
        }

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddSuccessGuardedRenderStep(PipelineStage.PostExecution, RenderCommitOutbox);
    }

    private static void RenderCommitOutbox(PipelineRenderContext context)
    {
        context.WriteBlankLine();

        context.DeclareVariable(
            "messageOutbox",
            context.GetOptionalServiceExpression("Blueprint.Messaging.IMessageOutbox"));

        context.If(
            "messageOutbox != null",
            branch =>
            {
                branch.WriteStatement("await messageOutbox.CommitAsync(context.OperationCancelled);");
            });
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "ICommand"
            && candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model");
    }

    private static bool HasDoNotPublishMessagesAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotPublishMessagesAttribute"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Messaging");
    }
}
