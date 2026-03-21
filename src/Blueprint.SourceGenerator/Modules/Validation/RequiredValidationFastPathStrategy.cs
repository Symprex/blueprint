using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal sealed class RequiredValidationFastPathStrategy : IValidationFastPathStrategy
{
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsRequiredAttribute(attribute.AttributeClass)
            || !IsSupportedRequiredAttribute(attribute))
        {
            return false;
        }

        if (property.Type.SpecialType == SpecialType.System_String)
        {
            rule = new ValidationFastPathRule(
                $"global::System.String.IsNullOrWhiteSpace({propertyAccess})",
                "new global::System.ComponentModel.DataAnnotations.RequiredAttribute()",
                $"!global::System.String.IsNullOrWhiteSpace({propertyAccess})");
            return true;
        }

        if (property.Type.IsReferenceType || ValidationSymbolHelpers.IsNullableValueType(property.Type))
        {
            rule = new ValidationFastPathRule(
                $"{propertyAccess} is null",
                "new global::System.ComponentModel.DataAnnotations.RequiredAttribute()",
                $"{propertyAccess} is not null");
            return true;
        }

        return false;
    }

    private static bool IsSupportedRequiredAttribute(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "AllowEmptyStrings")
            {
                if (namedArgument.Value.Value is bool allowEmptyStrings && allowEmptyStrings)
                {
                    return false;
                }

                continue;
            }

            return false;
        }

        return true;
    }
}
