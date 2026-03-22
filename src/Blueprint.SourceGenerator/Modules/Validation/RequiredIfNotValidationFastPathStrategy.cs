using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal sealed class RequiredIfNotValidationFastPathStrategy : IValidationFastPathStrategy
{
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)]
        out ValidationFastPathRule? rule)
    {
        rule = null;

        return ValidationSymbolHelpers.IsRequiredIfNotAttribute(attribute.AttributeClass)
               && ConditionalRequiredValidationFastPathStrategyHelpers.TryCreateRule(
                   property,
                   propertyAccess,
                   attribute,
                   "RequiredIfNotAttribute",
                   isRequiredWhenConditionMatches: false,
                   out rule);
    }
}
