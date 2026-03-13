using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.CodeWriter.Extensions;

/// <summary>
/// Provides small Roslyn helpers that convert syntax and attribute data into values useful during code generation.
/// </summary>
public static class RoslynExtensions
{
    /// <summary>
    /// Resolves the declared symbol for a type declaration syntax node.
    /// </summary>
    /// <param name="compilation">The compilation whose semantic model should be used when resolving symbols.</param>
    /// <param name="declarationSyntax">The type declaration syntax node whose symbol should be resolved.</param>
    /// <returns>The declared symbol for the supplied type declaration, or <see langword="null"/> when Roslyn cannot resolve one.</returns>
    public static TSymbol? GetSymbol<TSymbol>(this Compilation compilation, BaseTypeDeclarationSyntax declarationSyntax)
        where TSymbol : ISymbol
    {
        var model = compilation.GetSemanticModel(declarationSyntax.SyntaxTree);
        return (TSymbol?)model.GetDeclaredSymbol(declarationSyntax);
    }

    /// <summary>
    /// Looks up a named attribute argument and returns its TypedConstant value.
    /// </summary>
    /// <param name="attribute">The attribute to inspect.</param>
    /// <param name="name">The named argument to look up.</param>
    /// <returns>The named attribute argument value, or the default TypedConstant when no matching argument exists.</returns>
    public static TypedConstant GetAttributeValueByName(this AttributeData attribute, string name)
    {
        return attribute.NamedArguments.SingleOrDefault(arg => arg.Key == name).Value;
    }


    /// <summary>
    /// Looks up a named attribute argument and returns it as a string, falling back to a placeholder when missing.
    /// </summary>
    /// <param name="attribute">The attribute to inspect.</param>
    /// <param name="name">The named argument to look up.</param>
    /// <param name="placeholder">The fallback string to return when the requested attribute value is missing or null.</param>
    /// <returns>The attribute value as a string, or <paramref name="placeholder"/> when the named argument is missing or null.</returns>
    public static string GetAttributeValueByNameAsString(this AttributeData attribute, string name, string placeholder = "null")
    {
        var data = attribute.NamedArguments.SingleOrDefault(kvp => kvp.Key == name).Value;

        return data.Value is null ? placeholder : data.Value.ToString();
    }
}
