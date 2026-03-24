using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Reports legacy activation calls that are now redundant in the source-generated Blueprint architecture.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LegacyActivationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [
            MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation,
            MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration,
        ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(this.AnalyzeInvocation, Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Name.Identifier.ValueText != "AddOpenApi")
        {
            return;
        }

        // Migration analyzers intentionally key off stable legacy type names instead of namespaces because
        // consumers often wrap or re-expose the old Blueprint builder types from app-local namespaces while
        // moving toward the source-generated model. The BPMIG range is migration-specific, so favor broad
        // discovery over narrowly missing genuine upgrade targets.
        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (receiverType is not INamedTypeSymbol { Name: "BlueprintApiBuilder" })
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol
            || methodSymbol.Name != "AddOpenApi"
            || !SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, receiverType))
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 0 && methodSymbol.Parameters.Length == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation,
                memberAccess.Name.GetLocation()));
            return;
        }

        if (invocation.ArgumentList.Arguments.Count == 1
            && methodSymbol.Parameters.Length == 1
            && methodSymbol.Parameters[0].Type is INamedTypeSymbol
            {
                Name: "Action",
                TypeArguments.Length: 1,
            } configureCallback
            && configureCallback.TypeArguments[0] is INamedTypeSymbol { Name: "OpenApiOptions" })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration,
                memberAccess.Name.GetLocation()));
        }
    }
}
