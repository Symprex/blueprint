using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>RegularExpressionAttribute</c> when the pattern is a compile-time string literal and the property is
/// a simple string, emitting a generated <c>Regex.IsMatch</c> check that avoids the runtime reflection path.
/// </summary>
internal sealed class RegularExpressionValidationFastPathStrategy : IValidationFastPathStrategy
{
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

        rule = new ValidationFastPathRule(
            $"global::System.Text.RegularExpressions.Regex.IsMatch({propertyAccess}, @\"{escapedPattern}\") == false",
            $"new global::System.ComponentModel.DataAnnotations.RegularExpressionAttribute(@\"{escapedPattern}\")",
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }

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
