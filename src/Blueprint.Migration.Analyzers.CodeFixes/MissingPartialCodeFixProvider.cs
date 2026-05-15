using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Provides a code fix that adds the <c>partial</c> keyword to operation classes detected
/// by <see cref="Analyzers.MissingPartialAnalyzer"/> (BPMIG400). Supports "Fix all in solution"
/// for bulk migration of Signature 365's ~240+ operation classes.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingPartialCodeFixProvider))]
[Shared]
public sealed class MissingPartialCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.OperationMissingPartialKeyword.Id);

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

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is not TypeDeclarationSyntax typeDecl)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add 'partial' to {typeDecl.Identifier.Text}",
                    ct => AddPartialModifierAsync(context.Document, typeDecl, ct),
                    equivalenceKey: "BPMIG400_AddPartial"),
                diagnostic);
        }
    }

    private static async Task<Document> AddPartialModifierAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        // Insert 'partial' before the 'class' keyword, after any access modifiers
        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newModifiers = typeDecl.Modifiers.Add(partialToken);
        var newTypeDecl = typeDecl.WithModifiers(newModifiers);

        root = root.ReplaceNode(typeDecl, newTypeDecl);

        return document.WithSyntaxRoot(root);
    }
}
