using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>RegularExpressionAttribute</c> when the pattern is a compile-time string literal and the property is
/// a simple string, emitting a generated check against a cached <c>Regex</c> instance that avoids per-request
/// pattern compilation overhead from the <c>Regex.IsMatch(string, string)</c> static method.
/// </summary>
internal sealed class RegularExpressionValidationFastPathStrategy : IValidationFastPathStrategy
{
    private const string RegexFieldPlaceholder = "__CACHED_REGEX__";

    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)]
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsRegularExpressionAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || !TryGetSupportedPattern(attribute, out var pattern))
        {
            return false;
        }

        var escapedPattern = EscapeCSharpStringLiteral(pattern);

        // Use a placeholder that EmitCachedRegexFields will replace with the actual static field name.
        var regexExpression = $"new global::System.Text.RegularExpressions.Regex(@\"{escapedPattern}\", global::System.Text.RegularExpressions.RegexOptions.Compiled)";

        rule = new ValidationFastPathRule(
            $"{RegexFieldPlaceholder}.IsMatch({propertyAccess}) == false",
            $"new global::System.ComponentModel.DataAnnotations.RegularExpressionAttribute(@\"{escapedPattern}\")",
            GuardCondition: $"{propertyAccess} is not null",
            CachedAttributeTypeName: "System.ComponentModel.DataAnnotations.RegularExpressionAttribute",
            CachedRegexExpression: regexExpression);
        return true;
    }

    /// <summary>
    /// Gets the placeholder token that <see cref="ValidationModule"/> replaces with the actual cached regex field name.
    /// </summary>
    internal static string FieldPlaceholder => RegexFieldPlaceholder;

    private static bool TryGetSupportedPattern(AttributeData attribute, out string pattern)
    {
        pattern = string.Empty;

        // Only support the single-argument constructor RegularExpressionAttribute(string pattern).
        // Named arguments (e.g. ErrorMessage, MatchTimeoutInMilliseconds) force runtime fallback.
        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not string supportedPattern
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        pattern = supportedPattern;
        return true;
    }

    private static string EscapeCSharpStringLiteral(string value)
    {
        // In a verbatim string literal (@"..."), only double quotes need escaping by doubling them.
        return value.Replace("\"", "\"\"");
    }
}
