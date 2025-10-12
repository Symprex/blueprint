using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.AppDataModel.Model;

/// <summary>
/// The first stage of the <see cref="AppDataModelSourceGenerator"/> is to parse the class and attributes
/// that are marked with the BlueprintAppScanAttribute.
/// </summary>
/// <param name="Class">The data model class to be populated.</param>
/// <param name="ScanAttributes">An attribute model for each attribute found.</param>
internal record ParseOnlyDataModel(ParseOnlyBlueprintAppClassModel Class, EquatableList<ParseOnlyOperationScanAttributeModel> ScanAttributes);
