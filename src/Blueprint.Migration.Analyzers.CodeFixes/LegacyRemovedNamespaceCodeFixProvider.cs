using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Provides a code fix that removes using directives for legacy Blueprint namespaces that have
/// been entirely removed (no source-gen equivalent), as detected by BPMIG303.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LegacyRemovedNamespaceCodeFixProvider))]
[Shared]
public sealed class LegacyRemovedNamespaceCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.LegacyNamespaceRemoved.Id);

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

            var namespaceName = usingDirective.Name?.ToString() ?? "removed namespace";

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Remove 'using {namespaceName};'",
                    ct => RemoveUsingAsync(context.Document, usingDirective, ct),
                    equivalenceKey: $"BPMIG303_{namespaceName}"),
                diagnostic);
        }
    }

    private static async Task<Document> RemoveUsingAsync(
        Document document,
        UsingDirectiveSyntax usingDirective,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        root = root.RemoveNode(usingDirective, SyntaxRemoveOptions.KeepNoTrivia);

        return root is not null ? document.WithSyntaxRoot(root) : document;
    }
}
