using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Removes redundant legacy activation calls once a matching migration diagnostic has already proven the invocation is safe to drop.
/// Handles BPMIG001 (AddOpenApi), BPMIG002 (AddHateoasLinks), and BPMIG003 (AddAuthorisation).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LegacyActivationCodeFixProvider))]
[Shared]
public sealed class LegacyActivationCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableDictionary<string, string> DiagnosticToTitle = new Dictionary<string, string>
    {
        [MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.Id] = "Remove redundant AddOpenApi()",
        [MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation.Id] = "Remove redundant AddHateoasLinks()",
        [MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation.Id] = "Remove redundant AddAuthorisation()",
    }.ToImmutableDictionary();

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation.Id,
        MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation.Id,
        MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation.Id,
    ];

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

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!DiagnosticToTitle.TryGetValue(diagnostic.Id, out var title))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: cancellationToken => RemoveInvocationAsync(context.Document, root, invocation, cancellationToken),
                    equivalenceKey: title),
                diagnostic);
        }
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
