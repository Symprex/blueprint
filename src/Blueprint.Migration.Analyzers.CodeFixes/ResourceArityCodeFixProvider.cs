using System.Collections.Immutable;
using System.Composition;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.Migration.Analyzers.CodeFixes;

/// <summary>
/// Removes the resource key template (third argument) from <c>[Resource(noun, verb, "/path")]</c>
/// because the source-generated Blueprint model infers pure account-scoped auth automatically,
/// without a runtime string template.
/// </summary>
/// <remarks>
/// This mirrors <see cref="FromResourceCodeFixProvider"/>, but is more conservative because dropping the
/// third argument is not always behaviour-preserving for <c>[Resource]</c>: unlike <c>[FromResource]</c>'s
/// template (never load-bearing at runtime), a <c>[Resource]</c> resource key template can bind the
/// resource's own instance key (e.g. a bare <c>{Id}</c> or <c>{TenantId}</c> route placeholder) to a
/// specific property, and that binding is lost by a blind drop. Measured against Signature365's migrated
/// corpus, the fix is only safe when every placeholder in the template is an auth-claim reference
/// (<c>{Auth:...}</c> or <c>{Auth....}</c>) — those are inferred automatically and the whole argument was
/// dropped in every such case. Templates that reference any other property (a bare <c>{Id}</c> or
/// <c>{TenantId}</c>) were instead hand-rewritten to an explicit <c>{PropertyName:Auth.Claim}</c> binding
/// rather than dropped, so those stay diagnostic-only pending manual review. Named arguments that follow
/// the resource key template (e.g. <c>Details = "..."</c>) are preserved.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ResourceArityCodeFixProvider))]
[Shared]
public sealed class ResourceArityCodeFixProvider : CodeFixProvider
{
    private static readonly Regex PlaceholderPattern = new(@"\{([^{}]*)\}", RegexOptions.Compiled);

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.LegacyResourceArityRequiresMigration.Id);

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

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var attribute = node.FirstAncestorOrSelf<AttributeSyntax>();

            if (attribute?.ArgumentList is null
                || attribute.ArgumentList.Arguments.Count < 3
                || !IsSafeToDropResourceKeyTemplate(attribute.ArgumentList.Arguments[2], semanticModel, context.CancellationToken))
            {
                // A template that references anything other than an auth claim (a bare {Id} or
                // {TenantId}, for instance) usually needs an explicit rewrite rather than a drop;
                // leave the diagnostic for manual review instead of silently rewriting it.
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove resource key template from Resource",
                    ct => RemoveResourceKeyTemplateAsync(context.Document, attribute, ct),
                    equivalenceKey: "BPMIG203_RemoveResourceKeyTemplate"),
                diagnostic);
        }
    }

    private static bool IsSafeToDropResourceKeyTemplate(
        AttributeArgumentSyntax resourceKeyTemplateArgument,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel is null)
        {
            return false;
        }

        var constantValue = semanticModel.GetConstantValue(resourceKeyTemplateArgument.Expression, cancellationToken);
        if (!constantValue.HasValue || constantValue.Value is not string template)
        {
            // Not a literal we can reason about; leave for manual review.
            return false;
        }

        var placeholders = PlaceholderPattern.Matches(template);
        if (placeholders.Count == 0)
        {
            // No placeholder at all is outside the measured "pure auth-claim template" evidence;
            // leave for manual review rather than guessing.
            return false;
        }

        foreach (Match placeholder in placeholders)
        {
            var token = placeholder.Groups[1].Value;
            if (!token.StartsWith("Auth:", StringComparison.Ordinal)
                && !token.StartsWith("Auth.", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

        // Drop only the resource key template (the third argument). Any later named arguments,
        // such as a Details = "..." description, are preserved.
        var remainingArguments = attribute.ArgumentList.Arguments
            .Where(static (_, index) => index != 2);
        var newArgumentList = attribute.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(remainingArguments));

        var newAttribute = attribute.WithArgumentList(newArgumentList);
        root = root.ReplaceNode(attribute, newAttribute);

        return document.WithSyntaxRoot(root);
    }
}
