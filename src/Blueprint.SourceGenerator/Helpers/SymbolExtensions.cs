using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers
{
    /// <summary>
    /// Provides Roslyn symbol helpers used by the generators when they need stable semantic checks without duplicating
    /// low-level traversal logic.
    /// </summary>
    internal static class SymbolExtensions
    {
        /// <summary>
        /// Determines whether the class represented here has a property that has been implemented, checking by (exact)
        /// name that a non-abstract property exists.
        /// </summary>
        /// <param name="symbol">The class symbol to check.</param>
        /// <param name="propertyName">The name of the property to check for.</param>
        /// <returns>Whether a concrete property implementation exists.</returns>
        public static bool HasConcreteProperty(this INamedTypeSymbol symbol, string propertyName)
        {
            foreach (var member in symbol.GetMembers())
            {
                if (member is IPropertySymbol { IsAbstract: false } property && property.Name == propertyName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the given symbol inherits from the given base type, where the base type is the fully qualified
        /// name of the type (e.g. <c>System.String</c> or <c>Blueprint.Model.ApiResource</c>).
        /// </summary>
        /// <param name="symbol">The symbol to check from (a class, interface, etc.).</param>
        /// <param name="baseType">The base type to check against.</param>
        /// <returns>Whether the given symbol inherits from the given base type.</returns>
        public static bool InheritsFrom(this INamedTypeSymbol symbol, string baseType)
        {
            var current = symbol.BaseType;

            while (current != null)
            {
                if (current.ToString() == baseType)
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Checks whether the given symbol implements an interface identified by its fully qualified type name.
        /// </summary>
        /// <param name="symbol">The symbol whose interface list should be inspected.</param>
        /// <param name="interfaceType">The fully qualified interface type name to look for.</param>
        /// <returns><see langword="true"/> when the symbol implements the requested interface.</returns>
        public static bool ImplementsInterface(this INamedTypeSymbol symbol, string interfaceType)
        {
            return symbol.AllInterfaces.Any(i => i.ToString() == interfaceType);
        }

        /// <summary>
        /// Builds the full metadata name for a symbol so generator-side lookups stay stable across namespaces.
        /// </summary>
        /// <param name="symbol">The symbol whose metadata name should be rendered.</param>
        /// <returns>The fully qualified metadata name for the symbol.</returns>
        public static string ToFullMetadataName(this ISymbol symbol)
        {
            return symbol.ContainingNamespace.IsGlobalNamespace
                ? symbol.MetadataName
                : symbol.ContainingNamespace.ToDisplayString() + "." + symbol.MetadataName;
        }
    }
}
