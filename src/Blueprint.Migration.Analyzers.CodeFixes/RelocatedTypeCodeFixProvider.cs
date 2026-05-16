using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Provides a code fix that adds the missing <c>using</c> directive for Blueprint types
/// that moved from the root <c>Blueprint</c> namespace to a sub-namespace in source-generated
/// Blueprint. Triggered by <c>BPMIG402</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RelocatedTypeCodeFixProvider))]
[Shared]
public sealed class RelocatedTypeCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.RelocatedTypeNeedUsing.Id);

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
            if (!diagnostic.Properties.TryGetValue("TargetNamespace", out var targetNamespace) ||
                targetNamespace is null or { Length: 0 })
            {
                continue;
            }

            var title = $"Add 'using {targetNamespace};'";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    ct => AddUsingDirectiveAsync(context.Document, targetNamespace, ct),
                    equivalenceKey: $"BPMIG402_{targetNamespace}"),
                diagnostic);
        }
    }

    private static async Task<Document> AddUsingDirectiveAsync(
        Document document,
        string targetNamespace,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        // Check if the using already exists (could have been added by a parallel fix)
        var existingUsings = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(static u => u.StaticKeyword == default && u.Alias is null)
            .ToList();

        if (existingUsings.Any(u => u.Name?.ToString() == targetNamespace))
        {
            return document;
        }

        // Build the new using directive
        var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(targetNamespace))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        // Find the best insertion point: after the last Blueprint.* using, or after "using Blueprint;"
        var insertAfter = existingUsings
            .Where(u =>
            {
                var ns = u.Name?.ToString();
                return ns != null && ns.StartsWith("Blueprint", System.StringComparison.Ordinal);
            })
            .LastOrDefault();

        if (insertAfter is null)
        {
            // No Blueprint using found — insert at the end of all usings
            insertAfter = existingUsings.LastOrDefault();
        }

        if (insertAfter is not null)
        {
            // Insert after the anchor using
            root = root.InsertNodesAfter(insertAfter, new[] { newUsing });
        }
        else if (root is CompilationUnitSyntax compilationUnit)
        {
            // No usings at all — add at the top
            root = compilationUnit.AddUsings(newUsing);
        }

        return document.WithSyntaxRoot(root);
    }
}
