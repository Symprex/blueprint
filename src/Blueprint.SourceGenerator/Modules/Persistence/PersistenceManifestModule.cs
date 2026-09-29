using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules.Persistence;

/// <summary>
/// Contributes <c>persistence.saveChangesEnabled</c> to the per-operation debug manifest,
/// reflecting whether the UnitOfWork save-changes stage is active for the operation.
/// </summary>
/// <remarks>
/// Save-changes is active for every operation that does not carry <c>[DoNotSaveChanges]</c> (R138: queries included, as
/// in legacy). This module asks <see cref="UnitOfWorkModule.SavesUnitOfWork"/>, the check that module makes when deciding
/// whether to contribute to the pipeline, so the manifest accurately reflects the runtime behavior without depending on
/// pipeline plan metadata. (It said commands only, while the pipeline saved background tasks too.)
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
        var enabled = UnitOfWorkModule.SavesUnitOfWork(context.GetOperationTypeSymbol());

        context.AddEntry("persistence.saveChangesEnabled", enabled.ToString());
    }
}
