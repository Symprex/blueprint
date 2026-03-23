namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Identifies where a module sits in composition ordering when no explicit dependency edge exists.
/// </summary>
public enum BlueprintModuleCategory
{
    /// <summary>
    /// Foundation modules that establish the built-in Blueprint execution model.
    /// </summary>
    Core = 0,

    /// <summary>
    /// Consumer modules that layer opt-in behaviour on top of the core pipeline.
    /// </summary>
    Consumer = 1,
}
