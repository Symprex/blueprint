using Microsoft.CodeAnalysis;

namespace Blueprint.Migration.Analyzers;

/// <summary>
/// Zero-allocation namespace comparison helpers for Roslyn analyzers, avoiding
/// <c>ToDisplayString()</c> allocations. Mirrors the extension methods in
/// <c>Blueprint.SourceGenerator.Helpers.SymbolExtensions</c> for the migration
/// analyzer project which cannot reference the source generator assembly.
/// </summary>
internal static class NamespaceHelper
{
    /// <summary>
    /// Checks whether a symbol's containing namespace matches a two-part dotted name
    /// (e.g. "Blueprint.Model") without allocating a string.
    /// </summary>
    /// <param name="symbol">The symbol whose containing namespace is checked.</param>
    /// <param name="outerPart">The leftmost (root) namespace segment.</param>
    /// <param name="innerPart">The rightmost (leaf) namespace segment.</param>
    /// <returns><see langword="true"/> when the namespace matches <c>outerPart.innerPart</c> exactly.</returns>
    public static bool IsInNamespace(ISymbol symbol, string outerPart, string innerPart)
    {
        var ns = symbol.ContainingNamespace;

        return ns is not null
            && ns.Name == innerPart
            && ns.ContainingNamespace is { IsGlobalNamespace: false } parent
            && parent.Name == outerPart
            && parent.ContainingNamespace is { IsGlobalNamespace: true };
    }

    /// <summary>
    /// Checks whether a symbol's containing namespace matches a three-part dotted name
    /// (e.g. "Blueprint.Model.Attributes") without allocating a string.
    /// </summary>
    /// <param name="symbol">The symbol whose containing namespace is checked.</param>
    /// <param name="outerPart">The leftmost (root) namespace segment.</param>
    /// <param name="middlePart">The middle namespace segment.</param>
    /// <param name="innerPart">The rightmost (leaf) namespace segment.</param>
    /// <returns><see langword="true"/> when the namespace matches <c>outerPart.middlePart.innerPart</c> exactly.</returns>
    public static bool IsInNamespace(ISymbol symbol, string outerPart, string middlePart, string innerPart)
    {
        var ns = symbol.ContainingNamespace;

        return ns is not null
            && ns.Name == innerPart
            && ns.ContainingNamespace is { IsGlobalNamespace: false } middle
            && middle.Name == middlePart
            && middle.ContainingNamespace is { IsGlobalNamespace: false } outer
            && outer.Name == outerPart
            && outer.ContainingNamespace is { IsGlobalNamespace: true };
    }
}
