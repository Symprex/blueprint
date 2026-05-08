using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>LessThanAttribute</c> when the property type supports direct generated comparisons against an integer
/// literal threshold.
/// </summary>
internal sealed class LessThanValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsLessThanAttribute(attribute.AttributeClass)
            || !TryGetSupportedMaximum(attribute, out var maximumValue)
            || !SupportsDirectComparison(property.Type))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{propertyAccess} >= {maximumValue}",
            $"new global::Blueprint.Validation.LessThanAttribute({maximumValue})");
        return true;
    }

    private static bool TryGetSupportedMaximum(AttributeData attribute, out int maximumValue)
    {
        maximumValue = default;

        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not int supportedMaximum
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        maximumValue = supportedMaximum;
        return true;
    }

    private static bool SupportsDirectComparison(ITypeSymbol propertyType)
    {
        if (ValidationSymbolHelpers.IsNullableValueType(propertyType))
        {
            return false;
        }

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
