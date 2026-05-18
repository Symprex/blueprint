using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Migrates resource key template arguments from legacy <c>{Auth:...}</c> colon syntax to the
/// unified <c>{Auth....}</c> dot-notation syntax. The new syntax treats "Auth" as a well-known
/// first segment that resolves from the authorization context, eliminating the special-case colon
/// rule and making colon exclusively mean "fallback".
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

            if (argument is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Migrate {Auth:...} to {Auth....} dot-notation",
                    ct => MigrateAuthColonToDotAsync(context.Document, argument, ct),
                    equivalenceKey: "BPMIG201_MigrateAuthColonToDot"),
                diagnostic);
        }
    }

    private static async Task<Document> MigrateAuthColonToDotAsync(
        Document document,
        AttributeArgumentSyntax targetArgument,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        // Replace {Auth:X} with {Auth.X} in the string literal
        if (targetArgument.Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var originalText = literal.Token.ValueText;
            var migratedText = MigrateAuthPlaceholders(originalText);

            var newLiteral = SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(migratedText));

            root = root.ReplaceNode(literal, newLiteral);
        }

        return document.WithSyntaxRoot(root);
    }

    /// <summary>
    /// Replaces all occurrences of <c>{Auth:X}</c> with <c>{Auth.X}</c> in a template string.
    /// </summary>
    /// <param name="template">The resource key template containing legacy <c>{Auth:X}</c> placeholders.</param>
    /// <returns>The template with all <c>{Auth:</c> occurrences replaced by <c>{Auth.</c>.</returns>
    internal static string MigrateAuthPlaceholders(string template)
    {
        // Case-insensitive replacement of {Auth: → {Auth.
        var result = template;
        var searchIndex = 0;

        while (searchIndex < result.Length)
        {
            var braceIndex = result.IndexOf("{Auth:", searchIndex, StringComparison.OrdinalIgnoreCase);

            if (braceIndex < 0)
            {
                break;
            }

            // Replace the colon after "Auth" with a dot
            var colonPos = braceIndex + 5; // "{Auth" is 5 chars, colon is at position 5
            result = result.Substring(0, colonPos) + "." + result.Substring(colonPos + 1);
            searchIndex = colonPos + 1;
        }

        return result;
    }
}
