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

        // Skip any replacement the document already has. The fix has to be idempotent because
        // the namespace map overlaps - Blueprint.Http and Blueprint.OpenApi both map onto
        // Blueprint.Runtime.Http, and Blueprint.Http and Blueprint.Properties both onto
        // Blueprint.Model - so a document importing two legacy namespaces would otherwise gain
        // the same replacement twice. That matters most under fix-all, where every fix is
        // computed against one snapshot and cannot see what its siblings are inserting.
        var existingUsings = root
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(directive => directive != oldUsing)
            .Select(directive => directive.Name?.ToString())
            .Where(static name => name is not null)
            .ToImmutableHashSet(StringComparer.Ordinal);

        var newUsings = replacements
            .Where(replacement => !existingUsings.Contains(replacement))
            .Select(replacement => SyntaxFactory
                .UsingDirective(SyntaxFactory.ParseName(replacement))
                .WithLeadingTrivia(oldUsing.GetLeadingTrivia())
                .WithTrailingTrivia(oldUsing.GetTrailingTrivia()))
            .ToList();

        // Every replacement was already present, so the migration is complete for this directive
        // and only the legacy one needs to go. Removing it is still progress, which is what stops
        // the codemod's convergence loop from seeing the same diagnostic again next round.
        if (newUsings.Count == 0)
        {
            root = root.RemoveNode(oldUsing, SyntaxRemoveOptions.KeepNoTrivia)!;
            return document.WithSyntaxRoot(root);
        }

        root = newUsings.Count == 1
            ? root.ReplaceNode(oldUsing, newUsings[0])
            : root.ReplaceNode(oldUsing, newUsings);

        return document.WithSyntaxRoot(root);
    }
}
