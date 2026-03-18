namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Identifies where a module sits in composition ordering when no explicit dependency edge exists.
/// </summary>
public enum BlueprintModuleCategory
{
    Core = 0,
    Consumer = 1,
}
