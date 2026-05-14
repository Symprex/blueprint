using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Messaging;

/// <summary>
/// Contributes <c>messaging.outboxCommitEnabled</c> to the per-operation debug manifest,
/// reflecting whether the message outbox commit stage is active for the operation.
/// </summary>
/// <remarks>
/// Outbox commit is only active for command operations that do not carry <c>[DoNotPublishMessages]</c>.
/// This module replicates the same eligibility check that <see cref="MessageOutboxModule"/> performs
/// when deciding whether to contribute to the pipeline, so the manifest accurately reflects the
/// runtime behavior without depending on pipeline plan metadata.
/// </remarks>
internal sealed class MessageOutboxManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(MessageOutboxManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(MessageOutboxManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var isCommand = IsCommand(operationType);
        var hasOptOut = HasDoNotPublishMessagesAttribute(operationType);
        var enabled = isCommand && !hasOptOut;

        context.AddEntry("messaging.outboxCommitEnabled", enabled.ToString());
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "ICommand"
            && candidate.IsInNamespace("Blueprint", "Model"));
    }

    private static bool HasDoNotPublishMessagesAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotPublishMessagesAttribute"
            && attr.AttributeClass.IsInNamespace("Blueprint", "Messaging"));
    }
}
