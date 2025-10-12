using Blueprint.SourceGenerator.Generators.App.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Generators.App;

public sealed partial class AppDataModelSourceGenerator
{
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

        var model = ParseOnlyBlueprintAppClassModel.Create(classSymbol, context.TargetNode);

        return Result.Success(new ParseOnlyDataModel(model, [.. parseAttributes]));
    }
}


