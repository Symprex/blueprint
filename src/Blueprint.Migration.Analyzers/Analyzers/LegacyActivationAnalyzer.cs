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
        [MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation];

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
            || memberAccess.Name.Identifier.ValueText != "AddOpenApi"
            || invocation.ArgumentList.Arguments.Count != 0)
        {
            return;
        }

        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (receiverType is not INamedTypeSymbol { Name: "BlueprintApiBuilder" })
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol
            || methodSymbol.Name != "AddOpenApi"
            || methodSymbol.Parameters.Length != 0
            || !SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, receiverType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation,
            memberAccess.Name.GetLocation()));
    }
}
