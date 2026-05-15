using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects legacy Blueprint using directives that should be renamed for the
/// source-generated Blueprint framework. Covers namespace renames, sub-namespace
/// consolidation, and the <c>Blueprint.Configuration</c> → DI extension migration.
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
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Http.Formatters", ImmutableArray.Create("Blueprint.Runtime.Http")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Http.Infrastructure", ImmutableArray.Create("Blueprint.Runtime.Http")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Http.MessagePopulation", ImmutableArray.Create("Blueprint.Runtime.Http")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Http.Middleware", ImmutableArray.Create("Blueprint.Runtime.Middleware")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Authorisation", ImmutableArray.Create("Blueprint.Authorization")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Errors", ImmutableArray.Create("Blueprint")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.ThirdParty", ImmutableArray.Create("Blueprint.Utilities")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Configuration", ImmutableArray.Create("Microsoft.Extensions.DependencyInjection")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Properties", ImmutableArray.Create("Blueprint.Model")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.OpenApi", ImmutableArray.Create("Blueprint.Runtime.Http")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.CodeGen", ImmutableArray.Create<string>()),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Compiler", ImmutableArray.Create<string>()),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Compiler.Frames", ImmutableArray.Create<string>()),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Compiler.Model", ImmutableArray.Create<string>()),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Compiler.Util", ImmutableArray.Create<string>()),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Tasks.AspNetCore", ImmutableArray.Create("Blueprint.Runtime.Tasks")),
            new KeyValuePair<string, ImmutableArray<string>>("Blueprint.Tasks.Provider", ImmutableArray.Create("Blueprint.Tasks")),
        });

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.LegacyNamespaceUsing, MigrationDiagnosticDescriptors.LegacyNamespaceRemoved);

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

        if (replacements.IsEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.LegacyNamespaceRemoved,
                usingDirective.GetLocation(),
                namespaceName));
        }
        else
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.LegacyNamespaceUsing,
                usingDirective.GetLocation(),
                namespaceName,
                string.Join(", ", replacements)));
        }
    }
}
