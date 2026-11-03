using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Names the namespace a generated partial lives in, and qualifies names with it, for a type that may be declared in the
/// global namespace.
/// </summary>
/// <remarks>
/// Roslyn renders the global namespace as <c>&lt;global namespace&gt;</c>, which is neither a valid hint name nor a valid
/// <c>namespace</c> declaration. Every generator that names a file, emits a <c>namespace</c> or builds a metadata name
/// from a declared type's namespace goes through here, so a global type gets an empty namespace, no <c>namespace</c>
/// block, and an unprefixed hint name. An unprefixed hint name cannot collide with a namespaced type of the same name, whose hint name always
/// carries its namespace and a dot.
/// </remarks>
internal static class GeneratedNamespace
{
    /// <summary>
    /// Gets the name of the namespace that contains <paramref name="symbol"/>.
    /// </summary>
    /// <param name="symbol">The declared type or member.</param>
    /// <returns>The namespace's display name, or an empty string for the global namespace.</returns>
    public static string Of(ISymbol symbol)
    {
        return symbol.ContainingNamespace is null or { IsGlobalNamespace: true }
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
    }

    /// <summary>
    /// Prefixes a name with a namespace: a generated file's hint name, or a type's metadata name.
    /// </summary>
    /// <param name="namespaceName">The namespace from <see cref="Of" />, empty for the global namespace.</param>
    /// <param name="name">The name to qualify, for example <c>MyOperation_Descriptor.g.cs</c> or <c>MyApp</c>.</param>
    /// <returns><c>Namespace.Name</c>, or the name alone for the global namespace.</returns>
    public static string Qualify(string namespaceName, string name)
    {
        return namespaceName.Length == 0 ? name : $"{namespaceName}.{name}";
    }
}
