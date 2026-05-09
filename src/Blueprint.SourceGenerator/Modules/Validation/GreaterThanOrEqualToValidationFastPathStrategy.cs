using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>GreaterThanOrEqualToAttribute</c> when the property type supports direct generated comparisons against an
/// inclusive integer threshold. Nullable value types are supported via a guard condition.
/// </summary>
internal sealed class GreaterThanOrEqualToValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsGreaterThanOrEqualToAttribute(attribute.AttributeClass)
            || !TryGetSupportedMinimum(attribute, out var minimumValue, out var attributeExpression))
        {
            return false;
        }

        var isNullable = ValidationSymbolHelpers.IsNullableValueType(property.Type);
        var underlyingType = isNullable
            ? ValidationSymbolHelpers.GetNullableValueTypeArgument(property.Type)
            : property.Type;

        if (underlyingType is null || !SupportsDirectComparison(underlyingType))
        {
            return false;
        }

        var valueAccess = isNullable ? $"{propertyAccess}.Value" : propertyAccess;
        var guardCondition = isNullable ? $"{propertyAccess} is not null" : null;

        rule = new ValidationFastPathRule(
            $"{valueAccess} < {minimumValue}",
            attributeExpression,
            GuardCondition: guardCondition);
        return true;
    }

    private static bool TryGetSupportedMinimum(AttributeData attribute, out int minimumValue, [NotNullWhen(true)] out string? attributeExpression)
    {
        attributeExpression = null;
        minimumValue = default;

        if (attribute.NamedArguments.Length > 0
            || attribute.ConstructorArguments.Length is < 1 or > 2
            || attribute.ConstructorArguments[0].Value is not int supportedMinimum)
        {
            return false;
        }

        minimumValue = supportedMinimum;
        var minimumLiteral = supportedMinimum.ToString(CultureInfo.InvariantCulture);

        if (attribute.ConstructorArguments.Length == 1)
        {
            attributeExpression = $"new global::Blueprint.Validation.GreaterThanOrEqualToAttribute({minimumLiteral})";
            return true;
        }

        if (attribute.ConstructorArguments[1].Value is string errorMessage)
        {
            attributeExpression = $"new global::Blueprint.Validation.GreaterThanOrEqualToAttribute({minimumLiteral}, {CSharpSymbolDisplay.FormatLiteral(errorMessage, quote: true)})";
            return true;
        }

        return false;
    }

    private static bool SupportsDirectComparison(ITypeSymbol propertyType)
    {
        return propertyType.SpecialType is SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal;
    }
}
