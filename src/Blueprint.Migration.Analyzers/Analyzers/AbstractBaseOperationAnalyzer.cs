using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects abstract generic classes that directly implement <c>IQuery&lt;T&gt;</c> or <c>ICommand&lt;T&gt;</c>.
/// This pattern is now fully supported after <c>IOperation.Configure()</c> was changed from
/// <c>static abstract</c> to a source-generated method on concrete types only. The analyzer is retained
/// (disabled by default) for informational purposes during migration audits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbstractBaseOperationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.AbstractGenericBaseImplementsOperation);

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

        // Only abstract classes/records with open generic type parameters
        if (!typeSymbol.IsAbstract
            || typeSymbol.TypeKind != TypeKind.Class
            || typeSymbol.TypeParameters.Length == 0)
        {
            return;
        }

        // Check direct interfaces (not AllInterfaces) for IQuery<T> or ICommand<T>
        foreach (var iface in typeSymbol.Interfaces)
        {
            if (!IsQueryOrCommandInterface(iface))
            {
                continue;
            }

            // Find the syntax location of the offending base type for precise diagnostic placement
            var interfaceName = iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var location = FindBaseTypeSyntaxLocation(typeSymbol, iface, context.CancellationToken)
                           ?? typeSymbol.Locations.FirstOrDefault()
                           ?? Location.None;

            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.AbstractGenericBaseImplementsOperation,
                location,
                typeSymbol.Name,
                interfaceName));
        }
    }

    private static bool IsQueryOrCommandInterface(INamedTypeSymbol iface)
    {
        if (!iface.IsGenericType)
        {
            return false;
        }

        // Check the unbound generic definition name and namespace
        var original = iface.OriginalDefinition;

        return original.MetadataName is "IQuery`1" or "ICommand`1"
               && string.Equals(
                   original.ContainingNamespace?.ToDisplayString(),
                   "Blueprint.Model",
                   System.StringComparison.Ordinal);
    }

    private static Location? FindBaseTypeSyntaxLocation(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol targetInterface,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(cancellationToken) is not TypeDeclarationSyntax typeDecl
                || typeDecl.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in typeDecl.BaseList.Types)
            {
                var typeName = GetSimpleTypeName(baseType.Type);

                if (typeName is "IQuery" or "ICommand")
                {
                    return baseType.GetLocation();
                }
            }
        }

        return null;
    }

    private static string? GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            GenericNameSyntax generic => generic.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qualified => GetSimpleTypeName(qualified.Right),
            _ => null,
        };
    }
}
