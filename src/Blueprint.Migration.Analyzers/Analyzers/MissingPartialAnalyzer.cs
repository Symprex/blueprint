using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects operation classes implementing <c>ICommand&lt;T&gt;</c> or <c>IQuery&lt;T&gt;</c> that are
/// missing the <c>partial</c> keyword. Blueprint source generators emit partial class extensions
/// (descriptors, <c>Configure()</c> methods) that require the original declaration to be <c>partial</c>.
/// </summary>
/// <remarks>
/// This is the highest-volume migration code fix for Signature 365: every operation class (~240+)
/// needs <c>partial</c> added. The companion <c>MissingPartialCodeFixProvider</c> can apply this
/// across the entire solution in a single "Fix all" pass.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingPartialAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.OperationMissingPartialKeyword);

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

        // Only concrete, non-abstract classes (abstract bases are allowed to be non-partial)
        if (typeSymbol.IsAbstract || typeSymbol.TypeKind != TypeKind.Class)
        {
            return;
        }

        // Find the ICommand<T> or IQuery<T> interface (direct or inherited)
        var operationInterface = FindOperationInterface(typeSymbol);

        if (operationInterface is null)
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

        // The class implements ICommand/IQuery but is not partial — report diagnostic
        var interfaceName = operationInterface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var location = typeSymbol.Locations.FirstOrDefault() ?? Location.None;

        context.ReportDiagnostic(Diagnostic.Create(
            MigrationDiagnosticDescriptors.OperationMissingPartialKeyword,
            location,
            typeSymbol.Name,
            interfaceName));
    }

    private static INamedTypeSymbol? FindOperationInterface(INamedTypeSymbol typeSymbol)
    {
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var original = iface.OriginalDefinition;

            if (original.MetadataName is "IQuery`1" or "ICommand`1"
                && (NamespaceHelper.IsInNamespace(original, "Blueprint", "Model")
                    || NamespaceHelper.IsInNamespace(original, "Blueprint")))
            {
                return iface;
            }
        }

        return null;
    }
}
