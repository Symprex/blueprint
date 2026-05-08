using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>GreaterThanAttribute</c> when the property type supports direct generated comparisons against an integer
/// literal threshold. Nullable value types are supported via a guard condition.
/// </summary>
internal sealed class GreaterThanValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsGreaterThanAttribute(attribute.AttributeClass)
            || !TryGetSupportedMinimum(attribute, out var minimumValue))
        {
            return false;
        }

        var isNullable = ValidationSymbolHelpers.IsNullableValueType(property.Type);
        var underlyingType = isNullable
            ? ValidationSymbolHelpers.GetNullableValueTypeArgument(property.Type)!
            : property.Type;

        if (!SupportsDirectComparison(underlyingType))
        {
            return false;
        }

        var valueAccess = isNullable ? $"{propertyAccess}.Value" : propertyAccess;
        var guardCondition = isNullable ? $"{propertyAccess} is not null" : null;

        rule = new ValidationFastPathRule(
            $"{valueAccess} <= {minimumValue}",
            $"new global::Blueprint.Validation.GreaterThanAttribute({minimumValue})",
            GuardCondition: guardCondition);
        return true;
    }

    private static bool TryGetSupportedMinimum(AttributeData attribute, out int minimumValue)
    {
        minimumValue = default;

        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not int supportedMinimum
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        minimumValue = supportedMinimum;
        return true;
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
