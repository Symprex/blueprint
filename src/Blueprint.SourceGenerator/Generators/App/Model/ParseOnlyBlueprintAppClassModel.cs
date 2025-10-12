using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.App.Model;

/// <summary>
/// A parse-only (i.e. small and cacheable) representation of a BlueprintAppDataModel class that we need to generate
/// registrations for.
/// </summary>
/// <param name="Namespace">The namespace of the source class.</param>
/// <param name="Name">The name of the source class.</param>
/// <param name="MetadataName">The "metadata name" of the source class (i.e. includes namespace and name).</param>
internal record ParseOnlyBlueprintAppClassModel(
    string? Namespace,
    string Name,
    string MetadataName)
{
    public static ParseOnlyBlueprintAppClassModel Create(INamedTypeSymbol type, SyntaxNode? syntax)
    {
        return new ParseOnlyBlueprintAppClassModel(
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            Name: type.Name,
            MetadataName: type.ToFullMetadataName());
    }
}
