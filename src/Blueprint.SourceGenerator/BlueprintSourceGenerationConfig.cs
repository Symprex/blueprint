using Blueprint.SourceGenerator.AppDataModel;
using Blueprint.SourceGenerator.Modules;

namespace Blueprint.SourceGenerator;

/// <summary>
/// A configuration for the <see cref="AppDataModelSourceGenerator"/>.
/// </summary>
/// <param name="Modules">The modules to use to configure the source generator.</param>
public record BlueprintSourceGenerationConfig(IReadOnlyList<IBlueprintSourceGeneratorModule> Modules);
