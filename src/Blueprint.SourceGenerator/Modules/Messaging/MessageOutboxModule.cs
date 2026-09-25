using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a PostExecution render step that calls <c>IMessageOutbox.CommitAsync</c> after
/// successful handler execution. Participates for operations implementing <c>ICommand</c> or
/// <c>Blueprint.Tasks.IBackgroundTask</c> (not queries), and skips gracefully at runtime if no
/// <c>IMessageOutbox</c> is registered in DI.
/// </summary>
/// <remarks>
/// <para>
/// <c>IMessageOutbox</c> is Blueprint's own seam: legacy Blueprint had no equivalent. It is not the outbox legacy gave
/// its pipelines, which was the Marten session enlisted in the Rebus transaction by <c>UseMartenAsOutboxMiddleware</c>;
/// that one is reached through <c>Blueprint.Rebus.IRebusTransactionEnlistment</c>. What this module shares with legacy's
/// Rebus transaction scope is only the rule that messages enqueued during execution are committed only when the handler
/// succeeds. On failure the DI scope disposes the outbox, discarding pending messages.
/// </para>
/// <para>
/// Background tasks participate as commands do, so that messages a task enqueues through an <c>IMessageOutbox</c> are
/// committed rather than discarded with the scope. There is no legacy precedent for the seam itself; the choice follows
/// legacy treating the two alike for its own outbox, which it enlisted for background tasks as for commands (for example
/// Signature365 <c>8b40ecc961</c>, Billing <c>BillingSoftDeleteBackgroundTaskExecutorPipeline.cs:50-55</c>).
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

        // Commands and background tasks both commit the outbox; see the remarks for why tasks do.
        var participates = IsCommand(operationType) || IsBackgroundTask(operationType);

        if (!participates && HasDoNotPublishMessagesAttribute(operationType))
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    MessagingDiagnosticDescriptors.DoNotPublishMessagesOnNonCommand,
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));

            return;
        }

        if (!participates)
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
        context.WriteStatement("if (this.HasMessageOutbox)");
        context.WriteStatement("{");
        context.WriteStatement("    await CommitMessageOutboxAsync(context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);");
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

    private static bool HasDoNotPublishMessagesAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotPublishMessagesAttribute"
            && attr.AttributeClass.IsInNamespace("Blueprint", "Messaging"));
    }
}
