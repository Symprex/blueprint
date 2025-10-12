using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Generators.App.Model;

/// <summary>
/// The compilation model for the <see cref="AppDataModelSourceGenerator"/>. This represents a single
/// instance of a BlueprintAppDataModel that needs to be populated with API operations, along with all found
/// operations.
/// </summary>
/// <param name="Class">Details of the class itself.</param>
/// <param name="Operations">All "registrations", representing BlueprintAppScan attribute definitions.</param>
internal record BlueprintAppDataModelCompilationModel(
    ParseOnlyBlueprintAppClassModel Class,
    EquatableList<string> Operations);
