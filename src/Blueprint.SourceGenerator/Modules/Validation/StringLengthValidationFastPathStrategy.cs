using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal sealed class StringLengthValidationFastPathStrategy : IValidationFastPathStrategy
{
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsStringLengthAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || !TryGetSupportedStringLength(attribute, out var maximumLength, out var minimumLength))
        {
            return false;
        }

        var outOfRangeCondition = minimumLength > 0
            ? $"{propertyAccess}.Length < {minimumLength} || {propertyAccess}.Length > {maximumLength}"
            : $"{propertyAccess}.Length > {maximumLength}";
        var attributeExpression = minimumLength > 0
            ? $"new global::System.ComponentModel.DataAnnotations.StringLengthAttribute({maximumLength}) {{ MinimumLength = {minimumLength} }}"
            : $"new global::System.ComponentModel.DataAnnotations.StringLengthAttribute({maximumLength})";

        rule = new ValidationFastPathRule(
            $"({outOfRangeCondition})",
            attributeExpression,
            GuardCondition: $"{propertyAccess} is not null",
            WrapValidationAttributeExpression: true);
        return true;
    }

    private static bool TryGetSupportedStringLength(AttributeData attribute, out int maximumLength, out int minimumLength)
    {
        maximumLength = default;
        minimumLength = default;

        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not int supportedMaximumLength
            || supportedMaximumLength < 0)
        {
            return false;
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "MinimumLength")
            {
                if (namedArgument.Value.Value is not int supportedMinimumLength
                    || supportedMinimumLength < 0)
                {
                    return false;
                }

                minimumLength = supportedMinimumLength;
                continue;
            }

            return false;
        }

        if (minimumLength > supportedMaximumLength)
        {
            return false;
        }

        maximumLength = supportedMaximumLength;
        return true;
    }
}
