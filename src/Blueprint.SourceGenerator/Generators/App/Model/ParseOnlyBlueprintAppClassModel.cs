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
/// <param name="DeclarationLocation">The source location of the app class declaration.</param>
/// <param name="SerializerContextTypeName">The fully-qualified serializer context type declared by the app, when present.</param>
/// <param name="SerializerContextLocation">The source location of the serializer-context declaration, when present.</param>
/// <param name="SerializerContextBodyTypeNames">The distinct body payload CLR type names declared through the app-owned serializer context.</param>
/// <param name="SerializerContextBodyTypeTypeNames">The fully-qualified CLR type expressions declared through the app-owned serializer context.</param>
internal record ParseOnlyBlueprintAppClassModel(
    string? Namespace,
    string Name,
    string MetadataName,
    LocationInfo? DeclarationLocation,
    string? SerializerContextTypeName,
    LocationInfo? SerializerContextLocation,
    EquatableList<string> SerializerContextBodyTypeNames,
    EquatableList<string> SerializerContextBodyTypeTypeNames)
{
    /// <summary>
    /// Creates the cacheable app model used by later generator stages.
    /// </summary>
    /// <param name="type">The Blueprint app type being scanned.</param>
    /// <param name="syntax">The declaring syntax node for the app type, when available.</param>
    /// <param name="serializerContextTypeName">The fully-qualified serializer context type declared by the app, when present.</param>
    /// <param name="serializerContextLocation">The source location of the serializer-context declaration, when present.</param>
    /// <param name="serializerContextBodyTypeNames">The distinct body payload CLR type names declared through the app-owned serializer context.</param>
    /// <param name="serializerContextBodyTypeTypeNames">The fully-qualified CLR type expressions declared through the app-owned serializer context.</param>
    /// <returns>A compact model describing the app type.</returns>
    public static ParseOnlyBlueprintAppClassModel Create(
        INamedTypeSymbol type,
        SyntaxNode? syntax,
        string? serializerContextTypeName,
        LocationInfo? serializerContextLocation,
        EquatableList<string> serializerContextBodyTypeNames,
        EquatableList<string> serializerContextBodyTypeTypeNames)
    {
        var declarationLocation = type.Locations.Length > 0
            ? LocationInfo.CreateFrom(type.Locations[0])
            : syntax is null
                ? null
                : LocationInfo.CreateFrom(syntax);

        return new ParseOnlyBlueprintAppClassModel(
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            Name: type.Name,
            MetadataName: type.ToFullMetadataName(),
            DeclarationLocation: declarationLocation,
            SerializerContextTypeName: serializerContextTypeName,
            SerializerContextLocation: serializerContextLocation,
            SerializerContextBodyTypeNames: serializerContextBodyTypeNames,
            SerializerContextBodyTypeTypeNames: serializerContextBodyTypeTypeNames);
    }
}
