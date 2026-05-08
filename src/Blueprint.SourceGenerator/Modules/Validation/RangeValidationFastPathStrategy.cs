using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>RangeAttribute</c> when both minimum and maximum are integer literals and the property type supports
/// direct numeric comparisons, avoiding the runtime reflection path for the most common range validation pattern.
/// </summary>
internal sealed class RangeValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsRangeAttribute(attribute.AttributeClass)
            || !TryGetSupportedIntRange(attribute, out var minimum, out var maximum)
            || !SupportsDirectComparison(property.Type))
        {
            return false;
        }

        var minLiteral = minimum.ToString(CultureInfo.InvariantCulture);
        var maxLiteral = maximum.ToString(CultureInfo.InvariantCulture);

        rule = new ValidationFastPathRule(
            $"{propertyAccess} < {minLiteral} || {propertyAccess} > {maxLiteral}",
            $"new global::System.ComponentModel.DataAnnotations.RangeAttribute({minLiteral}, {maxLiteral})");
        return true;
    }

    private static bool TryGetSupportedIntRange(AttributeData attribute, out int minimum, out int maximum)
    {
        minimum = default;
        maximum = default;

        // Only support the RangeAttribute(int, int) constructor — the (double, double) and (Type, string, string)
        // overloads require runtime type conversion that cannot be safely lowered.
        if (attribute.ConstructorArguments.Length != 2
            || attribute.ConstructorArguments[0].Value is not int supportedMinimum
            || attribute.ConstructorArguments[1].Value is not int supportedMaximum
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        minimum = supportedMinimum;
        maximum = supportedMaximum;
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
