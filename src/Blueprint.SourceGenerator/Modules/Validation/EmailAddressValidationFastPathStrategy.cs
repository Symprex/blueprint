using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal sealed class EmailAddressValidationFastPathStrategy : IValidationFastPathStrategy
{
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsEmailAddressAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || attribute.NamedArguments.Length > 0
            || attribute.ConstructorArguments.Length > 0)
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"global::Blueprint.Validation.ValidationRuntime.IsValidEmailAddress({propertyAccess}) == false",
            "new global::System.ComponentModel.DataAnnotations.EmailAddressAttribute()");
        return true;
    }
}
