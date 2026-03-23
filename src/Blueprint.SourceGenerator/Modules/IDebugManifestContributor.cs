using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Marker capability for contributors that will participate in generated debug-manifest planning.
/// </summary>
public interface IDebugManifestContributor : IBlueprintContributor
{
    /// <summary>
    /// Contributes per-operation manifest data to the shared debug-manifest planning state.
    /// </summary>
    /// <param name="context">The manifest planning context for the current operation.</param>
    void ContributeToManifest(DebugManifestContributorContext context);
}
