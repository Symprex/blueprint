using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Shared helpers for detecting whether a type can be converted from a string value
/// at compile time. Used by PropertyOwnershipModule for route/query/header binding and by
/// TenantModule for non-string TenantId population.
/// </summary>
internal static class TypeConversionHelpers
{
    /// <summary>
    /// Returns <see langword="true"/> when the type implements <c>IParsable&lt;T&gt;</c>
    /// (<c>System.IParsable</c>), which ASP.NET Core uses for binding route/query values.
    /// </summary>
    /// <param name="type">The type symbol to inspect.</param>
    /// <returns><see langword="true"/> when the type implements <c>IParsable&lt;T&gt;</c>.</returns>
    internal static bool ImplementsIParsable(ITypeSymbol type)
    {
        return type.AllInterfaces.Any(
            static i => i is { IsGenericType: true, MetadataName: "IParsable`1", ContainingNamespace.Name: "System" });
    }

    /// <summary>
    /// Returns <see langword="true"/> when the type has a <c>static TryParse(string, out T)</c>
    /// or <c>static TryParse(string, IFormatProvider?, out T)</c> method, which ASP.NET Core
    /// uses as a fallback for binding route/query values.
    /// </summary>
    /// <param name="type">The type symbol to inspect.</param>
    /// <returns><see langword="true"/> when the type has a suitable <c>TryParse</c> method.</returns>
    internal static bool HasStaticTryParseMethod(ITypeSymbol type)
    {
        return type.GetMembers("TryParse")
            .OfType<IMethodSymbol>()
            .Any(static m =>
                m is { IsStatic: true, ReturnsVoid: false, ReturnType.SpecialType: SpecialType.System_Boolean }
                && m.Parameters.Length >= 2
                && m.Parameters[0].Type.SpecialType == SpecialType.System_String
                && m.Parameters[^1].RefKind == RefKind.Out);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the type has <c>[TypeConverter]</c>, which legacy
    /// Blueprint used for exception-based string conversion via
    /// <see cref="System.ComponentModel.TypeDescriptor"/>.
    /// </summary>
    /// <param name="type">The type symbol to inspect.</param>
    /// <returns><see langword="true"/> when the type has a <c>[TypeConverter]</c> attribute.</returns>
    internal static bool HasTypeConverterAttribute(ITypeSymbol type)
    {
        return type.GetAttributes().Any(
            static a => a.AttributeClass is
            {
                Name: "TypeConverterAttribute",
                ContainingNamespace: { Name: "ComponentModel", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } },
            });
    }

    /// <summary>
    /// Returns <see langword="true"/> when the type can be parsed from a string via
    /// <c>IParsable&lt;T&gt;</c> or a conventional <c>static TryParse</c> method.
    /// </summary>
    /// <param name="type">The type symbol to inspect.</param>
    /// <returns><see langword="true"/> when the type is parsable from a string.</returns>
    internal static bool IsStringParsable(ITypeSymbol type)
    {
        return ImplementsIParsable(type) || HasStaticTryParseMethod(type);
    }

    /// <summary>
    /// Returns a <c>TryParse</c> expression for the given type that evaluates to <c>true</c>
    /// on success and writes the parsed value into <paramref name="outVariable"/>.
    /// <para>
    /// Handles built-in scalars, enums, <c>IParsable&lt;T&gt;</c>, and conventional
    /// <c>static TryParse</c>. The caller should verify the type is parsable before calling.
    /// </para>
    /// </summary>
    /// <param name="typeSymbol">The type to generate a parse expression for.</param>
    /// <param name="stringExpression">The C# expression yielding the string to parse.</param>
    /// <param name="outVariable">The variable name for the parsed output.</param>
    /// <returns>A C# expression that evaluates to <c>true</c> on successful parse.</returns>
    internal static string GetTryParseExpression(ITypeSymbol typeSymbol, string stringExpression, string outVariable)
    {
        var typeDisplay = typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        return typeDisplay switch
        {
            "int" => $"int.TryParse({stringExpression}, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "long" => $"long.TryParse({stringExpression}, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "double" => $"double.TryParse({stringExpression}, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "float" => $"float.TryParse({stringExpression}, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "bool" => $"bool.TryParse({stringExpression}, out var {outVariable})",
            "decimal" => $"decimal.TryParse({stringExpression}, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            "System.Guid" => $"System.Guid.TryParse({stringExpression}, out var {outVariable})",
            "System.DateTime" => $"System.DateTime.TryParse({stringExpression}, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var {outVariable})",
            "System.DateTimeOffset" => $"System.DateTimeOffset.TryParse({stringExpression}, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var {outVariable})",
            _ when typeSymbol.TypeKind == TypeKind.Enum =>
                $"System.Enum.TryParse<{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>({stringExpression}, out var {outVariable})",
            _ when ImplementsIParsable(typeSymbol) =>
                $"{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.TryParse({stringExpression}, System.Globalization.CultureInfo.InvariantCulture, out var {outVariable})",
            _ when HasStaticTryParseMethod(typeSymbol) =>
                $"{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.TryParse({stringExpression}, out var {outVariable})",
            _ => $"int.TryParse({stringExpression}, out var {outVariable})", // fallback — should not be reached for supported types
        };
    }

    /// <summary>
    /// Returns a static lambda expression compatible with <c>TryParseFunc&lt;T&gt;</c>
    /// for use with <c>PipelineHelpers.BindRouteValue</c> and similar binding helpers.
    /// The lambda wraps the type-specific TryParse call so the delegate can be cached by the compiler.
    /// </summary>
    /// <param name="typeSymbol">The target type to generate a parse lambda for.</param>
    /// <returns>A C# source string containing a <c>static</c> lambda expression.</returns>
    internal static string GetTryParseLambda(ITypeSymbol typeSymbol)
    {
        var typeDisplay = typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        return typeDisplay switch
        {
            "int" => "static (string _s, out int _v) => int.TryParse(_s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _v)",
            "long" => "static (string _s, out long _v) => long.TryParse(_s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _v)",
            "double" => "static (string _s, out double _v) => double.TryParse(_s, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out _v)",
            "float" => "static (string _s, out float _v) => float.TryParse(_s, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out _v)",
            "bool" => "static (string _s, out bool _v) => bool.TryParse(_s, out _v)",
            "decimal" => "static (string _s, out decimal _v) => decimal.TryParse(_s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _v)",
            "System.Guid" => "static (string _s, out System.Guid _v) => System.Guid.TryParse(_s, out _v)",
            "System.DateTime" => "static (string _s, out System.DateTime _v) => System.DateTime.TryParse(_s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _v)",
            "System.DateTimeOffset" => "static (string _s, out System.DateTimeOffset _v) => System.DateTimeOffset.TryParse(_s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _v)",
            _ when typeSymbol.TypeKind == TypeKind.Enum =>
                $"static (string _s, out {typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} _v) => System.Enum.TryParse(_s, out _v)",
            _ when ImplementsIParsable(typeSymbol) =>
                $"static (string _s, out {typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} _v) => {typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.TryParse(_s, System.Globalization.CultureInfo.InvariantCulture, out _v)",
            _ when HasStaticTryParseMethod(typeSymbol) =>
                $"static (string _s, out {typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} _v) => {typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.TryParse(_s, out _v)",
            _ => "static (string _s, out int _v) => int.TryParse(_s, out _v)", // fallback
        };
    }
}
