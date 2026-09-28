using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Tenancy;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Incremental source generator that discovers operation handlers and generates executor pipelines.
/// </summary>
public partial class OperationExecutorPipelineSourceGenerator
{
    private const string TenancyAttributeMetadataName = "Blueprint.Model.BlueprintAppTenancyAttribute";

    /// <summary>
    /// Parses a BlueprintApp class from the syntax context.
    /// </summary>
    private static Result<AppInfo>? ParseBlueprintApp(GeneratorAttributeSyntaxContext context)
    {
        // Should not happen as the attribute can only be applied to a class
        if (context.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        // Must be partial
        if (!classSymbol.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is BaseTypeDeclarationSyntax { Modifiers: { Count: > 0 } modifiers } &&
                modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            return Result<AppInfo>.Failure(
                new DiagnosticInfo(
                    DiagnosticDescriptors.MustBePartial,
                    LocationInfo.CreateFrom(classSymbol.Locations[0]),
                    new EquatableList<string>([classSymbol.Name])));
        }

        var appInfo = new AppInfo(
            classSymbol.Name,
            classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol,
            GetTenantIdTypeName(classSymbol));

        return Result.Success(appInfo);
    }

    /// <summary>
    /// Reads the app's <c>[BlueprintAppTenancy(TenantIdType = typeof(T))]</c> as the fully qualified name of <c>T</c>, a
    /// <c>Nullable&lt;T&gt;</c> read as its <c>T</c>. A missing attribute, or one without a <c>TenantIdType</c>, is
    /// <see langword="null"/>: no restriction on which <c>TenantId</c> types are filled.
    /// </summary>
    private static string? GetTenantIdTypeName(INamedTypeSymbol classSymbol)
    {
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToFullMetadataName() != TenancyAttributeMetadataName)
            {
                continue;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "TenantIdType" && namedArgument.Value.Value is ITypeSymbol tenantIdType)
                {
                    return TenantIdConventionHelper.UnwrapNullable(tenantIdType).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        return null;
    }
}
