using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

public sealed partial class OperationExecutorPipelineSourceGenerator
{
    /// <summary>
    /// Parses a BlueprintApp class from the syntax context.
    /// </summary>
    private static Result<AppInfo>? ParseBlueprintApp(GeneratorAttributeSyntaxContext context)
    {
        // Should not happen as the attribute can only be applied to a class
        if (context.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        // Must be partial
        if (!classSymbol.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is BaseTypeDeclarationSyntax { Modifiers: { Count: > 0 } modifiers } &&
                modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            return Result<AppInfo>.Failure(
                new DiagnosticInfo(
                    DiagnosticDescriptors.MustBePartial,
                    LocationInfo.CreateFrom(classSymbol.Locations[0]),
                    new EquatableList<string>(new[] { classSymbol.Name })));
        }

        var appInfo = new AppInfo(
            classSymbol.Name,
            classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol);

        return Result.Success(appInfo);
    }
}
