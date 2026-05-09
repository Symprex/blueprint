using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects usage of legacy Blueprint API patterns that have been replaced in the
/// source-generated framework:
/// <list type="bullet">
///   <item><c>IMiddlewareBuilder</c> → <c>IBlueprintSourceGeneratorModule</c> (BPMIG301)</item>
///   <item><c>BlueprintApiBuilder</c> → <c>AddBlueprintApi&lt;TApp, TExec&gt;</c> (BPMIG302)</item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LegacyApiAnalyzer : DiagnosticAnalyzer
{
    private const string MiddlewareBuilderName = "IMiddlewareBuilder";
    private const string BlueprintApiBuilderName = "BlueprintApiBuilder";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            MigrationDiagnosticDescriptors.LegacyMiddlewareBuilderUsage,
            MigrationDiagnosticDescriptors.LegacyBlueprintApiBuilderUsage);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeBaseList, SyntaxKind.BaseList);
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
    }

    /// <summary>
    /// Flags classes that implement IMiddlewareBuilder in their base list.
    /// </summary>
    private static void AnalyzeBaseList(SyntaxNodeAnalysisContext context)
    {
        var baseList = (BaseListSyntax)context.Node;

        foreach (var baseType in baseList.Types)
        {
            var typeName = GetSimpleTypeName(baseType.Type);

            if (typeName == MiddlewareBuilderName)
            {
                var declaringType = baseList.Parent as TypeDeclarationSyntax;
                var className = declaringType?.Identifier.Text ?? "Unknown";

                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDiagnosticDescriptors.LegacyMiddlewareBuilderUsage,
                    baseType.GetLocation(),
                    className));
            }
        }
    }

    /// <summary>
    /// Flags references to BlueprintApiBuilder in parameters, fields, and variable declarations.
    /// </summary>
    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        var identifier = (IdentifierNameSyntax)context.Node;

        if (identifier.Identifier.Text != BlueprintApiBuilderName)
        {
            return;
        }

        // Only flag in meaningful positions: parameters, fields, properties, variable declarations, method returns
        var parent = identifier.Parent;

        if (parent is null)
        {
            return;
        }

        // Skip using directives — those are handled by LegacyNamespaceAnalyzer
        if (identifier.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
        {
            return;
        }

        // Only flag in type positions (parameter types, return types, field/property types, local var types)
        if (parent is ParameterSyntax or
            VariableDeclarationSyntax or
            QualifiedNameSyntax or
            TypeConstraintSyntax)
        {
            ReportBlueprintApiBuilderDiagnostic(context, identifier);
            return;
        }

        // Also flag when it appears as a type argument or return type
        if (parent is TypeArgumentListSyntax or
            SimpleBaseTypeSyntax)
        {
            ReportBlueprintApiBuilderDiagnostic(context, identifier);
        }
    }

    private static void ReportBlueprintApiBuilderDiagnostic(SyntaxNodeAnalysisContext context, IdentifierNameSyntax identifier)
    {
        var containingMember = identifier.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        var memberName = containingMember switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax => "field",
            TypeDeclarationSyntax type => type.Identifier.Text,
            _ => "member",
        };

        context.ReportDiagnostic(Diagnostic.Create(
            MigrationDiagnosticDescriptors.LegacyBlueprintApiBuilderUsage,
            identifier.GetLocation(),
            memberName));
    }

    private static string? GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => null,
        };
    }
}
