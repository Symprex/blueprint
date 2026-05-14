using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.CodeFixes;

/// <summary>
/// Adds the <c>partial</c> modifier to class declarations that trigger BP001, BP002, BP004, or BP104
/// diagnostics. These diagnostics fire when a Blueprint operation, API resource, app, or event family
/// class is not partial — a requirement for source generation.
/// </summary>
/// <remarks>
/// Supports "Fix All" via <see cref="WellKnownFixAllProviders.BatchFixer"/> so that an entire
/// S365-scale solution (540+ operations) can be fixed in one pass from the IDE or via
/// <c>dotnet format analyzers</c>.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddPartialModifierCodeFixProvider))]
[Shared]
public sealed class AddPartialModifierCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add 'partial' modifier";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            "BP001",  // ApiResource must be partial
            "BP002",  // App class must be partial
            "BP005",  // Operation/ResourceEvent must be partial (descriptor generation)
            "BP104"); // Operation must be partial (pipeline generation)

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
            var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();

            if (typeDecl is null)
            {
                continue;
            }

            if (typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: _ => AddPartialModifierAsync(context.Document, root, typeDecl),
                    equivalenceKey: Title),
                diagnostic);
        }
    }

    private static Task<Document> AddPartialModifierAsync(
        Document document,
        SyntaxNode root,
        TypeDeclarationSyntax typeDecl)
    {
        // Insert 'partial' before the type keyword (class/struct/record), preserving trivia
        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newModifiers = typeDecl.Modifiers.Add(partialToken);
        var newTypeDecl = typeDecl.WithModifiers(newModifiers);

        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
