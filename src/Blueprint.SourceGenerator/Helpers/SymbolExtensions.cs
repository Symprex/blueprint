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
            // Walk the type hierarchy — an inherited non-abstract override satisfies the check.
            var current = symbol;
            while (current != null)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is IPropertySymbol { IsAbstract: false } property && property.Name == propertyName)
                    {
                        return true;
                    }
                }

                current = current.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Checks if the given symbol inherits from the given base type, where the base type is the fully qualified
        /// name of the type (e.g. <c>System.String</c> or <c>Blueprint.Model.ApiResource</c>).
        /// Compares <see cref="ISymbol.MetadataName"/> first (cheap) and only falls back to the full display
        /// string when the short name matches — avoids per-ancestor string allocation in the common non-match case.
        /// </summary>
        /// <param name="symbol">The symbol to check from (a class, interface, etc.).</param>
        /// <param name="baseType">The base type to check against.</param>
        /// <returns>Whether the given symbol inherits from the given base type.</returns>
        public static bool InheritsFrom(this INamedTypeSymbol symbol, string baseType)
        {
            var lastDot = baseType.LastIndexOf('.');
            var simpleName = lastDot >= 0 ? baseType.Substring(lastDot + 1) : baseType;

            var current = symbol.BaseType;

            while (current != null)
            {
                if (current.MetadataName == simpleName && current.ToString() == baseType)
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Checks whether the given symbol implements an interface identified by its fully qualified type name.
        /// Compares <see cref="ISymbol.MetadataName"/> first (cheap) to short-circuit on the common non-match case.
        /// </summary>
        /// <param name="symbol">The symbol whose interface list should be inspected.</param>
        /// <param name="interfaceType">The fully qualified interface type name to look for.</param>
        /// <returns><see langword="true"/> when the symbol implements the requested interface.</returns>
        public static bool ImplementsInterface(this INamedTypeSymbol symbol, string interfaceType)
        {
            var lastDot = interfaceType.LastIndexOf('.');
            var simpleName = lastDot >= 0 ? interfaceType.Substring(lastDot + 1) : interfaceType;

            return symbol.AllInterfaces.Any(i => i.MetadataName == simpleName && i.ToString() == interfaceType);
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
                : $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.MetadataName}";
        }

        /// <summary>
        /// Determines whether the given type is assignable to the target type, checking direct equality,
        /// interface implementation (including open generic definitions), and base-type hierarchy.
        /// Consolidates the three duplicate implementations that previously existed in FindOperations,
        /// FindHandlers, and TypedHttpClientExtensionsGenerator.
        /// </summary>
        /// <param name="type">The concrete type to test.</param>
        /// <param name="target">The interface or class type to test assignability against.</param>
        /// <returns><see langword="true"/> when <paramref name="type"/> is assignable to <paramref name="target"/>.</returns>
        public static bool IsAssignableTo(this INamedTypeSymbol type, INamedTypeSymbol target)
        {
            if (SymbolEqualityComparer.Default.Equals(type, target))
            {
                return true;
            }

            if (target.TypeKind == TypeKind.Interface)
            {
                foreach (var iface in type.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(iface, target)
                        || (iface.IsGenericType && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, target)))
                    {
                        return true;
                    }
                }

                return false;
            }

            var baseType = type.BaseType;
            while (baseType != null)
            {
                if (SymbolEqualityComparer.Default.Equals(baseType, target))
                {
                    return true;
                }

                baseType = baseType.BaseType;
            }

            return false;
        }
    }
}
