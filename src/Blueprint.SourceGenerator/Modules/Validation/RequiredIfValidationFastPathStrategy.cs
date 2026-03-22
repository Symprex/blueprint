using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal sealed class RequiredIfValidationFastPathStrategy : IValidationFastPathStrategy
{
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)]
        out ValidationFastPathRule? rule)
    {
        rule = null;

        return ValidationSymbolHelpers.IsRequiredIfAttribute(attribute.AttributeClass)
               && ConditionalRequiredValidationFastPathStrategyHelpers.TryCreateRule(
                   property,
                   propertyAccess,
                   attribute,
                   "RequiredIfAttribute",
                   isRequiredWhenConditionMatches: true,
                   out rule);
    }
}
