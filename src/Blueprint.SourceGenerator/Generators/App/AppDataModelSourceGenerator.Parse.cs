using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Generators.App;

/// <summary>
/// Incremental source generator that discovers Blueprint app scan declarations and emits app registrations.
/// </summary>
public sealed partial class AppDataModelSourceGenerator
{
    private const string SerializerContextAttributeMetadataName = "Blueprint.Model.BlueprintAppSerializerContextAttribute";
    private const string JsonSerializableAttributeMetadataName = "System.Text.Json.Serialization.JsonSerializableAttribute";

    private static Result<ParseOnlyDataModel>? ParseDataModelWithScanAttributes(GeneratorAttributeSyntaxContext context)
    {
        // Should not happen as the attribute can only be applied to a class
        if (context.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        // We have a Scan attribute but it is not applied to a partial class
        if (!classSymbol.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is BaseTypeDeclarationSyntax { Modifiers: { Count: > 0 } modifiers } && modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            return Result<ParseOnlyDataModel>.Failure(
                new DiagnosticInfo(_missingPartialModifierRule, classSymbol.Locations[0], [classSymbol.Name]));
        }

        var parseAttributes = new ParseOnlyOperationScanAttributeModel[context.Attributes.Length];
        for (var i = 0; i < context.Attributes.Length; i++)
        {
            var attribute = ParseOnlyOperationScanAttributeModel.Create(context.Attributes[i]);
            parseAttributes[i] = attribute;

            if (parseAttributes[i].HasErrors)
            {
                return Result<ParseOnlyDataModel>.Failure(
                    new DiagnosticInfo(_cannotParseScanAttribute, parseAttributes[i].Location));
            }
        }

        var serializerContext = GetSerializerContext(classSymbol);
        var model = ParseOnlyBlueprintAppClassModel.Create(
            classSymbol,
            context.TargetNode,
            serializerContext.TypeName,
            serializerContext.Location,
            serializerContext.BodyTypeNames,
            serializerContext.BodyTypeTypeNames);

        return Result.Success(new ParseOnlyDataModel(model, [.. parseAttributes]));
    }

    private static SerializerContextDeclaration GetSerializerContext(INamedTypeSymbol classSymbol)
    {
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToFullMetadataName() != SerializerContextAttributeMetadataName)
            {
                continue;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "JsonSerializerContextType"
                    && namedArgument.Value.Value is INamedTypeSymbol serializerContextType)
                {
                    var serializerContextLocation = attribute.ApplicationSyntaxReference is null
                        ? null
                        : LocationInfo.CreateFrom(attribute.ApplicationSyntaxReference.GetSyntax());

                    return new SerializerContextDeclaration(
                        serializerContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        serializerContextLocation,
                        GetSerializerContextBodyTypeNames(serializerContextType),
                        GetSerializerContextBodyTypeTypeNames(serializerContextType));
                }
            }
        }

        return new SerializerContextDeclaration(null, null, new EquatableList<string>(), new EquatableList<string>());
    }

    private static EquatableList<string> GetSerializerContextBodyTypeNames(INamedTypeSymbol serializerContextType)
    {
        var bodyTypeNames = serializerContextType.GetAttributes()
            .Where(static attribute => string.Equals(attribute.AttributeClass?.ToDisplayString(), JsonSerializableAttributeMetadataName, StringComparison.Ordinal))
            .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
            .OfType<ITypeSymbol>()
            .Select(static typeSymbol => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new EquatableList<string>(bodyTypeNames);
    }

    private static EquatableList<string> GetSerializerContextBodyTypeTypeNames(INamedTypeSymbol serializerContextType)
    {
        var bodyTypeNames = serializerContextType.GetAttributes()
            .Where(static attribute => string.Equals(attribute.AttributeClass?.ToDisplayString(), JsonSerializableAttributeMetadataName, StringComparison.Ordinal))
            .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
            .OfType<ITypeSymbol>()
            .Select(static typeSymbol => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new EquatableList<string>(bodyTypeNames);
    }

    private sealed record SerializerContextDeclaration(
        string? TypeName,
        LocationInfo? Location,
        EquatableList<string> BodyTypeNames,
        EquatableList<string> BodyTypeTypeNames);
}


