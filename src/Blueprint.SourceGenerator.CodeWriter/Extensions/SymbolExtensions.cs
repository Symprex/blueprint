using System;
using System.Linq;
using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.CodeWriter.Extensions;

/// <summary>
/// Represents symbol extensions.
/// </summary>
public static class SymbolExtensions
{
    /// <summary>
    /// Gets qualified type name.
    /// </summary>
    /// <param name="typeSymbol">The type symbol value.</param>
    /// <returns>The generated result.</returns>
    public static string GetQualifiedTypeName(this ITypeSymbol typeSymbol)
    {
        var type = typeSymbol.GetTypeName();
        if (!type.Contains(".") || typeSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return type;
        }

        var @namespace = namedTypeSymbol.ContainingNamespace.ToString();
        var typeName = namedTypeSymbol.Name;

        if (typeSymbol.ContainingType is not null)
        {
            return $"{GetQualifiedTypeName(typeSymbol.ContainingType)}.{typeSymbol.Name}";
        }

        var fullyQualifiedName = $"{@namespace}.{typeName}";

        if (namedTypeSymbol.IsGenericType)
        {
            var generics = namedTypeSymbol.TypeArguments.Select(x => GetQualifiedTypeName(x));
            var baseName = fullyQualifiedName;
            var nullable = string.Empty;
            if (baseName.EndsWith("?", StringComparison.InvariantCulture))
            {
                baseName = baseName.Substring(0, baseName.Length - 1);
                nullable = "?";
            }

            fullyQualifiedName = $"{baseName}<{string.Join(", ", generics)}>{nullable}";
        }

        if (fullyQualifiedName.Contains(".") && !fullyQualifiedName.StartsWith("global::", StringComparison.InvariantCulture))
        {
            fullyQualifiedName = $"global::{fullyQualifiedName}";
        }

        return fullyQualifiedName;
    }

    /// <summary>
    /// Performs the is nullable operation.
    /// </summary>
    /// <param name="type">The type value.</param>
    /// <returns>The generated result.</returns>
    public static bool IsNullable(this ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return true;

        return ((type as INamedTypeSymbol)?.IsGenericType ?? false)
            && type.OriginalDefinition.ToDisplayString().Equals("System.Nullable<T>", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the is nullable operation.
    /// </summary>
    /// <param name="type">The type value.</param>
    /// <param name="nullableType">The nullable type value.</param>
    /// <returns>The generated result.</returns>
    public static bool IsNullable(this ITypeSymbol type, out ITypeSymbol? nullableType)
    {
        if (type.IsNullable())
        {
            nullableType = type.NullableAnnotation == NullableAnnotation.Annotated ? type : ((INamedTypeSymbol)type).TypeArguments.First();
            return true;
        }
        else
        {
            nullableType = null;
            return false;
        }
    }
}
