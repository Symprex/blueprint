using System.Collections.Immutable;
using System.Composition;
using Blueprint.Migration.Analyzers.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Provides a code fix that replaces legacy Blueprint namespace using directives with
/// their source-generated equivalents detected by <see cref="LegacyNamespaceAnalyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LegacyNamespaceCodeFixProvider))]
[Shared]
public sealed class LegacyNamespaceCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.LegacyNamespaceUsing.Id);

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

            if (node is not UsingDirectiveSyntax usingDirective)
            {
                continue;
            }

            var oldNamespace = usingDirective.Name?.ToString();

            if (oldNamespace is null || !LegacyNamespaceAnalyzer.NamespaceReplacements.TryGetValue(oldNamespace, out var replacements))
            {
                continue;
            }

            var title = $"Replace with {string.Join(" + ", replacements.Select(r => $"using {r};"))}";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    ct => ReplaceNamespaceAsync(context.Document, usingDirective, replacements, ct),
                    equivalenceKey: $"BPMIG300_{oldNamespace}"),
                diagnostic);
        }
    }

    private static async Task<Document> ReplaceNamespaceAsync(
        Document document,
        UsingDirectiveSyntax oldUsing,
        ImmutableArray<string> replacements,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        // Build the replacement using directives
        var newUsings = new List<UsingDirectiveSyntax>();

        foreach (var replacement in replacements)
        {
            var newName = SyntaxFactory.ParseName(replacement);
            var newUsing = SyntaxFactory.UsingDirective(newName)
                .WithLeadingTrivia(oldUsing.GetLeadingTrivia())
                .WithTrailingTrivia(oldUsing.GetTrailingTrivia());
            newUsings.Add(newUsing);
        }

        // Replace the old using with the new one(s)
        if (newUsings.Count == 1)
        {
            root = root.ReplaceNode(oldUsing, newUsings[0]);
        }
        else
        {
            root = root.ReplaceNode(oldUsing, newUsings);
        }

        return document.WithSyntaxRoot(root);
    }
}
