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
            // Walk the type hierarchy looking for an explicit override of the named property.
            // We deliberately skip the base virtual declaration so that the source generator
            // can emit a more efficient compile-time override for subclasses that don't
            // already provide one.
            var current = symbol;
            while (current != null)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is IPropertySymbol { IsAbstract: false, IsOverride: true } property
                        && property.Name == propertyName)
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
            // For nested types, walk up the containment chain to include enclosing types.
            if (symbol is INamedTypeSymbol namedType && namedType.ContainingType is not null)
            {
                return $"{namedType.ContainingType.ToFullMetadataName()}.{namedType.MetadataName}";
            }

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

        /// <summary>
        /// Checks whether a symbol's containing namespace matches a two-part dotted name (e.g. "Blueprint.Validation")
        /// without allocating a <see cref="string"/> via <c>ToDisplayString()</c>.
        /// </summary>
        /// <param name="symbol">The symbol whose namespace is tested.</param>
        /// <param name="outerPart">The outermost namespace segment (e.g. "Blueprint").</param>
        /// <param name="innerPart">The inner namespace segment (e.g. "Validation").</param>
        /// <returns><see langword="true"/> when the namespace matches <c>outerPart.innerPart</c> exactly.</returns>
        public static bool IsInNamespace(this ISymbol symbol, string outerPart, string innerPart)
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
        /// (e.g. "System.ComponentModel.DataAnnotations") without allocating a <see cref="string"/>.
        /// </summary>
        /// <param name="symbol">The symbol whose namespace is tested.</param>
        /// <param name="outerPart">The outermost namespace segment (e.g. "System").</param>
        /// <param name="middlePart">The middle namespace segment (e.g. "ComponentModel").</param>
        /// <param name="innerPart">The innermost namespace segment (e.g. "DataAnnotations").</param>
        /// <returns><see langword="true"/> when the namespace matches <c>outerPart.middlePart.innerPart</c> exactly.</returns>
        public static bool IsInNamespace(this ISymbol symbol, string outerPart, string middlePart, string innerPart)
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

        /// <summary>
        /// Checks whether a symbol's containing namespace is a single root namespace (e.g. "Blueprint")
        /// without allocating a <see cref="string"/>.
        /// </summary>
        /// <param name="symbol">The symbol whose namespace is tested.</param>
        /// <param name="namespaceName">The single namespace segment (e.g. "Blueprint").</param>
        /// <returns><see langword="true"/> when the namespace matches <paramref name="namespaceName"/> exactly.</returns>
        public static bool IsInRootNamespace(this ISymbol symbol, string namespaceName)
        {
            var ns = symbol.ContainingNamespace;

            return ns is not null
                && ns.Name == namespaceName
                && ns.ContainingNamespace is { IsGlobalNamespace: true };
        }

        /// <summary>
        /// Checks whether a symbol's containing namespace matches a four-part dotted name
        /// (e.g. "System.Text.Json.Serialization") without allocating a <see cref="string"/>.
        /// </summary>
        /// <param name="symbol">The symbol whose namespace is tested.</param>
        /// <param name="part1">The outermost namespace segment (e.g. "System").</param>
        /// <param name="part2">The second segment (e.g. "Text").</param>
        /// <param name="part3">The third segment (e.g. "Json").</param>
        /// <param name="part4">The innermost segment (e.g. "Serialization").</param>
        /// <returns><see langword="true"/> when the namespace matches <c>part1.part2.part3.part4</c> exactly.</returns>
        public static bool IsInNamespace(this ISymbol symbol, string part1, string part2, string part3, string part4)
        {
            var ns = symbol.ContainingNamespace;

            return ns is not null
                && ns.Name == part4
                && ns.ContainingNamespace is { IsGlobalNamespace: false } level3
                && level3.Name == part3
                && level3.ContainingNamespace is { IsGlobalNamespace: false } level2
                && level2.Name == part2
                && level2.ContainingNamespace is { IsGlobalNamespace: false } level1
                && level1.Name == part1
                && level1.ContainingNamespace is { IsGlobalNamespace: true };
        }

        /// <summary>
        /// Checks whether a namespace symbol has "System" as its root (outermost) segment,
        /// without allocating a <see cref="string"/>. Matches "System", "System.Collections",
        /// "System.Text.Json", etc.
        /// </summary>
        /// <param name="ns">The namespace symbol to test.</param>
        /// <returns><see langword="true"/> when the root namespace is "System".</returns>
        public static bool IsSystemNamespace(this INamespaceSymbol? ns)
        {
            if (ns is null || ns.IsGlobalNamespace)
            {
                return false;
            }

            // Walk up to the root namespace.
            var current = ns;

            while (current.ContainingNamespace is { IsGlobalNamespace: false } parent)
            {
                current = parent;
            }

            return current.Name == "System";
        }
    }
}
