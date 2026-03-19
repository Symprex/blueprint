using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Marker capability for contributors that will participate in pipeline planning.
/// </summary>
public interface IPipelineContributor : IBlueprintContributor
{
    /// <summary>
    /// Contributes pipeline plan state for the current operation.
    /// </summary>
    /// <param name="context">The contribution context.</param>
    void Contribute(PipelineContributorContext context);
}
