using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Source: https://github.com/dominikjeske/Samples/blob/main/SourceGenerators/HomeCenter.SourceGenerators/Extensions/RoslynExtensions.cs
/// </summary>
#pragma warning disable SA1600
internal static class RoslynExtensions
{
    private static IEnumerable<ITypeSymbol> GetBaseTypesAndThis(this ITypeSymbol type)
    {
        var current = type;

        while (current != null)
        {
            yield return current;
            current = current.BaseType;
        }
    }

    public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol type)
    {
        return type.GetBaseTypesAndThis().SelectMany(n => n.GetMembers());
    }

    public static CompilationUnitSyntax? GetCompilationUnit(this SyntaxNode syntaxNode)
    {
        return syntaxNode.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();
    }

    public static string GetClassName(this ClassDeclarationSyntax proxy)
    {
        return proxy.Identifier.Text;
    }

    public static string GetClassModifier(this ClassDeclarationSyntax proxy)
    {
        return proxy.Modifiers.ToFullString().Trim();
    }

    public static bool HaveAttribute(this ClassDeclarationSyntax classSyntax, string attributeName)
    {
        return classSyntax.AttributeLists.Count > 0 &&
               classSyntax.AttributeLists.SelectMany(al => al.Attributes
                       .Where(a => (a?.Name as IdentifierNameSyntax)?.Identifier.Text == attributeName))
                   .Any();
    }

    public static string GetNamespace(this CompilationUnitSyntax root)
    {
        return root
            .ChildNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .First()
            .Name
            .ToString();
    }

    public static List<string> GetUsings(this CompilationUnitSyntax root)
    {
        return root.ChildNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(n => n.Name!.ToString())
            .ToList();
    }

    // Thanks to https://www.codeproject.com/Articles/871704/Roslyn-Code-Analysis-in-Easy-Samples-Part-2
    public static string? GetWhereStatement(this ITypeParameterSymbol typeParameterSymbol)
    {
        var result = $"where {typeParameterSymbol.Name} : ";

        var constraints = string.Empty;

        var isFirstConstraint = true;

        if (typeParameterSymbol.HasReferenceTypeConstraint)
        {
            constraints += "class";
            isFirstConstraint = false;
        }

        if (typeParameterSymbol.HasValueTypeConstraint)
        {
            constraints += "struct";
            isFirstConstraint = false;
        }

        foreach (var constraintType in typeParameterSymbol.ConstraintTypes)
        {
            // if not first constraint prepend with comma
            if (!isFirstConstraint)
            {
                constraints += ", ";
            }
            else
            {
                isFirstConstraint = false;
            }

            constraints += constraintType.GetFullTypeString();
        }

        if (string.IsNullOrEmpty(constraints))
        {
            return null;
        }

        result += constraints;

        return result;
    }

    private static string GetFullTypeString(this ISymbol type)
    {
        return type.Name + type.GetTypeArgsStr(symbol => ((INamedTypeSymbol)symbol).TypeArguments);
    }

    private static string GetTypeArgsStr(this ISymbol symbol, Func<ISymbol, ImmutableArray<ITypeSymbol>> typeArgGetter)
    {
        var typeArgs = typeArgGetter(symbol);

        if (!typeArgs.Any())
        {
            return string.Empty;
        }

        var stringsToAdd = new List<string>();

        foreach (var arg in typeArgs)
        {
            string strToAdd;

            if (arg is ITypeParameterSymbol typeParameterSymbol)
            {
                // this is a generic argument
                strToAdd = typeParameterSymbol.Name;
            }
            else
            {
                // this is a generic argument value.
                var namedTypeSymbol = arg as INamedTypeSymbol;
                strToAdd = namedTypeSymbol!.GetFullTypeString();
            }

            stringsToAdd.Add(strToAdd);
        }

        var result = $"<{string.Join(", ", stringsToAdd)}>";

        return result;
    }
}
#pragma warning restore SA1600
