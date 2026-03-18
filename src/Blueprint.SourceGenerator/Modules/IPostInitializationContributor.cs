using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes post-initialization source such as marker attributes.
/// </summary>
public interface IPostInitializationContributor : IBlueprintContributor
{
    /// <summary>
    /// Registers any marker attributes or static source required before generation starts.
    /// </summary>
    /// <param name="initializationContext">The post-initialization context.</param>
    void RegisterMarkerAttributes(IncrementalGeneratorPostInitializationContext initializationContext);
}
