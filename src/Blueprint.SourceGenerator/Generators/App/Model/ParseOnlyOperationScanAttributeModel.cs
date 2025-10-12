using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.App.Model;

/// <summary>
/// Represents a single BlueprintAppScanAttribute.
/// </summary>
/// <param name="AssemblyOfTypeName">The assembly from which to search for operations.</param>
/// <param name="Location">The cacheable location of the attribute.</param>
/// <param name="HasErrors">Whether the attribute has errors.</param>
internal record ParseOnlyOperationScanAttributeModel(
    string? AssemblyOfTypeName,
    Location Location,
    bool HasErrors)
{
    /// <summary>
    /// Creates a new <see cref="ParseOnlyOperationScanAttributeModel"/> from the given <see cref="AttributeData"/>.
    /// </summary>
    /// <param name="attribute">The attribute to parse.</param>
    /// <returns>A new <see cref="ParseOnlyOperationScanAttributeModel"/>.</returns>
    public static ParseOnlyOperationScanAttributeModel Create(AttributeData attribute)
    {
        var assemblyType = attribute.NamedArguments.FirstOrDefault(static a => a.Key == "FromAssemblyOf").Value.Value as INamedTypeSymbol;

        var assemblyOfTypeName = assemblyType?.ToFullMetadataName();

        var syntax = attribute.ApplicationSyntaxReference!.SyntaxTree;
        var textSpan = attribute.ApplicationSyntaxReference.Span;
        var location = Location.Create(syntax, textSpan);

        var hasError = assemblyType is { TypeKind: TypeKind.Error };

        return new(
            assemblyOfTypeName,
            location,
            hasError);
    }
}
