using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Removes the resource key template (second argument) from <c>[FromResource(typeof(X), "/path")]</c>
/// because the source-generated Blueprint model derives auth/resource metadata at compile time
/// instead of relying on a runtime string template.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FromResourceCodeFixProvider))]
[Shared]
public sealed class FromResourceCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.LegacyFromResourceUsageRequiresMigration.Id);

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
            var attribute = node.FirstAncestorOrSelf<AttributeSyntax>();

            if (attribute?.ArgumentList is null || attribute.ArgumentList.Arguments.Count < 2)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove resource key template from FromResource",
                    ct => RemoveResourceKeyTemplateAsync(context.Document, attribute, ct),
                    equivalenceKey: "BPMIG200_RemoveResourceKeyTemplate"),
                diagnostic);
        }
    }

    private static async Task<Document> RemoveResourceKeyTemplateAsync(
        Document document,
        AttributeSyntax attribute,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || attribute.ArgumentList is null)
        {
            return document;
        }

        // Keep only the first argument (the type reference), discard all subsequent arguments
        // which carry the legacy resource key template string.
        var firstArgument = attribute.ArgumentList.Arguments[0];
        var newArgumentList = attribute.ArgumentList.WithArguments(
            SyntaxFactory.SeparatedList(new[] { firstArgument }));

        var newAttribute = attribute.WithArgumentList(newArgumentList);
        root = root.ReplaceNode(attribute, newAttribute);

        return document.WithSyntaxRoot(root);
    }
}
