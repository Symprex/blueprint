using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal sealed class MaxLengthValidationFastPathStrategy : IValidationFastPathStrategy
{
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsMaxLengthAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || !TryGetSupportedMaxLength(attribute, out var maxLength))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{propertyAccess}.Length > {maxLength}",
            $"new global::System.ComponentModel.DataAnnotations.MaxLengthAttribute({maxLength})",
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }

    private static bool TryGetSupportedMaxLength(AttributeData attribute, out int maxLength)
    {
        maxLength = default;

        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not int supportedMaxLength
            || supportedMaxLength < 0
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        maxLength = supportedMaxLength;
        return true;
    }
}
