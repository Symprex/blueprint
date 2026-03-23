using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Removes redundant legacy activation calls once a matching migration diagnostic has already proven the invocation is safe to drop.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LegacyActivationCodeFixProvider))]
[Shared]
public sealed class LegacyActivationCodeFixProvider : CodeFixProvider
{
    private const string RemoveAddOpenApiTitle = "Remove redundant AddOpenApi()";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.Id];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var invocation = root.FindNode(context.Span).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: RemoveAddOpenApiTitle,
                createChangedDocument: cancellationToken => RemoveInvocationAsync(context.Document, root, invocation, cancellationToken),
                equivalenceKey: RemoveAddOpenApiTitle),
            context.Diagnostics);
    }

    private static Task<Document> RemoveInvocationAsync(
        Document document,
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var updatedRoot = invocation.Parent switch
        {
            ExpressionStatementSyntax expressionStatement => root.RemoveNode(
                expressionStatement,
                SyntaxRemoveOptions.KeepExteriorTrivia | SyntaxRemoveOptions.AddElasticMarker)!,
            _ when invocation.Expression is MemberAccessExpressionSyntax memberAccess => root.ReplaceNode(
                invocation,
                memberAccess.Expression.WithTriviaFrom(invocation)),
            _ => root,
        };

        return Task.FromResult(document.WithSyntaxRoot(updatedRoot));
    }
}
