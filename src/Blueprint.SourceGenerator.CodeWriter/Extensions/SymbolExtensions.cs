using System;
using System.Linq;
using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.CodeWriter.Extensions;

/// <summary>
/// Provides Roslyn symbol helpers for turning type information into code-friendly names and nullability metadata.
/// </summary>
public static class SymbolExtensions
{
    /// <summary>
    /// Formats a type symbol as a fully qualified name suitable for generated source.
    /// </summary>
    /// <param name="typeSymbol">The Roslyn type symbol to convert into emitted type text.</param>
    /// <returns>The fully qualified type name formatted for generated source.</returns>
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
    /// Determines whether a Roslyn type symbol should be treated as nullable when generating code.
    /// </summary>
    /// <param name="type">The type to emit in generated source.</param>
    /// <returns>True when the inspected type should be treated as nullable in generated code; otherwise false.</returns>
    public static bool IsNullable(this ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated)
            return true;

        return ((type as INamedTypeSymbol)?.IsGenericType ?? false)
            && type.OriginalDefinition.ToDisplayString().Equals("System.Nullable<T>", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether a Roslyn type symbol should be treated as nullable when generating code.
    /// </summary>
    /// <param name="type">The type to emit in generated source.</param>
    /// <param name="nullableType">The nullable type used by this operation.</param>
    /// <returns>True when the inspected type should be treated as nullable in generated code; otherwise false.</returns>
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
