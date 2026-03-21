using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal sealed class MinLengthValidationFastPathStrategy : IValidationFastPathStrategy
{
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsMinLengthAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || !TryGetSupportedMinLength(attribute, out var minLength))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{propertyAccess}.Length < {minLength}",
            $"new global::System.ComponentModel.DataAnnotations.MinLengthAttribute({minLength})",
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }

    private static bool TryGetSupportedMinLength(AttributeData attribute, out int minLength)
    {
        minLength = default;

        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Value is not int supportedMinLength
            || supportedMinLength < 0
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        minLength = supportedMinLength;
        return true;
    }
}
