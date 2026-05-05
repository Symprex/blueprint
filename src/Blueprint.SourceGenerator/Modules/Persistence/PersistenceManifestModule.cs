using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Persistence;

/// <summary>
/// Contributes <c>persistence.saveChangesEnabled</c> to the per-operation debug manifest,
/// reflecting whether the UnitOfWork save-changes stage is active for the operation.
/// </summary>
/// <remarks>
/// Save-changes is only active for command operations that do not carry <c>[DoNotSaveChanges]</c>.
/// This module replicates the same eligibility check that <see cref="UnitOfWorkModule"/> performs
/// when deciding whether to contribute to the pipeline, so the manifest accurately reflects the
/// runtime behavior without depending on pipeline plan metadata.
/// </remarks>
internal sealed class PersistenceManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(PersistenceManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(PersistenceManifestModule);

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
        var hasOptOut = HasDoNotSaveChangesAttribute(operationType);
        var enabled = isCommand && !hasOptOut;

        context.AddEntry("persistence.saveChangesEnabled", enabled.ToString());
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
