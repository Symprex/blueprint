using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects legacy Blueprint type names that have been renamed in the source-generated framework.
/// Currently handles:
/// <list type="bullet">
///   <item><c>ResourceEvent&lt;T&gt;</c> → <c>ResourceEventResult&lt;T&gt;</c> (BPMIG203)</item>
///   <item><c>ResourceEventChangeType</c> → <c>ChangeType</c> (BPMIG204)</item>
/// </list>
/// These are high-volume renames in S365 (131+ handlers use <c>ResourceEvent&lt;T&gt;</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LegacyTypeRenameAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Maps legacy simple type names to their replacement names and the diagnostic to emit.
    /// The analyzer deliberately matches by simple name to catch both qualified and unqualified usages.
    /// </summary>
    private static readonly ImmutableArray<(string LegacyName, string NewName, DiagnosticDescriptor Descriptor)> TypeRenames =
    [
        ("ResourceEvent", "ResourceEventResult", MigrationDiagnosticDescriptors.LegacyResourceEventTypeRename),
        ("ResourceEventChangeType", "ChangeType", MigrationDiagnosticDescriptors.LegacyChangeTypeRename),
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            MigrationDiagnosticDescriptors.LegacyResourceEventTypeRename,
            MigrationDiagnosticDescriptors.LegacyChangeTypeRename);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
        context.RegisterSyntaxNodeAction(AnalyzeGenericName, SyntaxKind.GenericName);
    }

    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        var identifier = (IdentifierNameSyntax)context.Node;

        // Skip using directives — those are handled by LegacyNamespaceAnalyzer
        if (identifier.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
        {
            return;
        }

        foreach (var (legacyName, newName, descriptor) in TypeRenames)
        {
            if (identifier.Identifier.Text == legacyName)
            {
                ReportDiagnostic(context, identifier, identifier.Identifier.Text, newName, descriptor);
                return;
            }
        }
    }

    private static void AnalyzeGenericName(SyntaxNodeAnalysisContext context)
    {
        var genericName = (GenericNameSyntax)context.Node;

        // Skip using directives
        if (genericName.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
        {
            return;
        }

        foreach (var (legacyName, newName, descriptor) in TypeRenames)
        {
            if (genericName.Identifier.Text == legacyName)
            {
                ReportDiagnostic(context, genericName, genericName.Identifier.Text, newName, descriptor);
                return;
            }
        }
    }

    private static void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        SyntaxNode node,
        string legacyName,
        string newName,
        DiagnosticDescriptor descriptor)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            node.GetLocation(),
            legacyName,
            newName));
    }
}
