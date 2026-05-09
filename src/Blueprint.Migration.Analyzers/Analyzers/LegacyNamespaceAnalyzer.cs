using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects <c>using Blueprint.Http;</c>, <c>using Blueprint.Authorisation;</c>,
/// <c>using Blueprint.Errors;</c>, and <c>using Blueprint.ThirdParty;</c> directives
/// that should be renamed for the source-generated Blueprint framework.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LegacyNamespaceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Mapping from legacy namespace to the set of replacement namespaces.
    /// When multiple replacements exist, the code fix adds all of them.
    /// </summary>
    public static readonly ImmutableDictionary<string, ImmutableArray<string>> NamespaceReplacements =
        ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Http", ImmutableArray.Create("Blueprint.Model", "Blueprint.Runtime.Http")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Authorisation", ImmutableArray.Create("Blueprint.Authorization")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Errors", ImmutableArray.Create("Blueprint")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.ThirdParty", ImmutableArray.Create("Blueprint.Utilities")),
        });

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.LegacyNamespaceUsing);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;

        // Only plain using directives (not static, not alias)
        if (usingDirective.StaticKeyword != default || usingDirective.Alias is not null)
        {
            return;
        }

        var namespaceName = usingDirective.Name?.ToString();

        if (namespaceName is null || !NamespaceReplacements.ContainsKey(namespaceName))
        {
            return;
        }

        var replacements = NamespaceReplacements[namespaceName];

        context.ReportDiagnostic(Diagnostic.Create(
            MigrationDiagnosticDescriptors.LegacyNamespaceUsing,
            usingDirective.GetLocation(),
            namespaceName,
            string.Join(", ", replacements)));
    }
}
