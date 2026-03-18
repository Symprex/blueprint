namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Root contract for a source-generator module.
/// </summary>
public interface IBlueprintSourceGeneratorModule
{
    /// <summary>
    /// Gets the stable module identifier.
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// Gets the default composition category for the module.
    /// </summary>
    BlueprintModuleCategory Category { get; }
}
