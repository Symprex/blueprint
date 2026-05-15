using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Removes the resource key template argument containing <c>{Auth:...}</c> placeholders from
/// <c>[Resource(noun, verb, "/path/{Auth:...}")]</c> and <c>[FromResource(typeof(X), "/path/{Auth:...}")]</c>
/// attributes because the source-generated Blueprint model resolves auth context at compile time
/// through the tenant module rather than runtime string expansion.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AuthPlaceholderCodeFixProvider))]
[Shared]
public sealed class AuthPlaceholderCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.LegacyAuthPlaceholderUsageRequiresMigration.Id);

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
            var argument = node.FirstAncestorOrSelf<AttributeArgumentSyntax>();
            var attribute = argument?.FirstAncestorOrSelf<AttributeSyntax>();

            if (attribute?.ArgumentList is null || argument is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove {Auth:...} resource key template argument",
                    ct => RemoveAuthPlaceholderArgumentAsync(context.Document, attribute, argument, ct),
                    equivalenceKey: "BPMIG201_RemoveAuthPlaceholder"),
                diagnostic);
        }
    }

    private static async Task<Document> RemoveAuthPlaceholderArgumentAsync(
        Document document,
        AttributeSyntax attribute,
        AttributeArgumentSyntax targetArgument,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || attribute.ArgumentList is null)
        {
            return document;
        }

        // Remove the argument that contains the {Auth:...} placeholder
        var newArguments = attribute.ArgumentList.Arguments.Remove(targetArgument);
        var newArgumentList = attribute.ArgumentList.WithArguments(newArguments);
        var newAttribute = attribute.WithArgumentList(newArgumentList);

        root = root.ReplaceNode(attribute, newAttribute);

        return document.WithSyntaxRoot(root);
    }
}
