using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects concrete classes inheriting from <c>ApiResource</c> that are missing the <c>partial</c>
/// keyword. Blueprint source generators emit a partial class extension with the <c>Object</c> property
/// override, so the original declaration must be <c>partial</c>.
/// </summary>
/// <remarks>
/// This is the companion to <see cref="MissingPartialAnalyzer"/> (BPMIG400), covering API resource
/// classes (~60 in Signature 365) that also need the <c>partial</c> keyword. The shared
/// <c>MissingPartialCodeFixProvider</c> handles both BPMIG400 and BPMIG401 in a single "Fix all" pass.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResourceMissingPartialAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.ResourceMissingPartialKeyword);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
        {
            return;
        }

        // Only concrete, non-abstract classes
        if (typeSymbol.IsAbstract || typeSymbol.TypeKind != TypeKind.Class)
        {
            return;
        }

        // Walk the base type chain looking for ApiResource
        if (!InheritsFromApiResource(typeSymbol))
        {
            return;
        }

        // Check if any declaration of this type already has the partial modifier
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax typeDecl
                && typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return;
            }
        }

        var location = typeSymbol.Locations.FirstOrDefault() ?? Location.None;

        context.ReportDiagnostic(Diagnostic.Create(
            MigrationDiagnosticDescriptors.ResourceMissingPartialKeyword,
            location,
            typeSymbol.Name));
    }

    private static bool InheritsFromApiResource(INamedTypeSymbol typeSymbol)
    {
        var baseType = typeSymbol.BaseType;

        while (baseType is not null)
        {
            if (baseType.Name == "ApiResource"
                && NamespaceHelper.IsInNamespace(baseType, "Blueprint", "Model"))
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }
}
