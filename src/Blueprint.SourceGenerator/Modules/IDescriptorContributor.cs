using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes descriptor diagnostics, metadata, and rendering steps for an operation.
/// </summary>
public interface IDescriptorContributor : IBlueprintContributor
{
    /// <summary>
    /// Contributes descriptor plan state for the current operation.
    /// </summary>
    /// <param name="context">The contribution context.</param>
    void Contribute(DescriptorContributorContext context);
}
