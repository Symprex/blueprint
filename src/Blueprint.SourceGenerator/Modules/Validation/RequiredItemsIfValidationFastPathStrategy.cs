using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>RequiredItemsIfAttribute</c> by delegating to the shared conditional-required collection lowering helper.
/// </summary>
internal sealed class RequiredItemsIfValidationFastPathStrategy : IValidationFastPathStrategy
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

        return ValidationSymbolHelpers.IsRequiredItemsIfAttribute(attribute.AttributeClass)
               && ConditionalRequiredValidationFastPathStrategyHelpers.TryCreateItemsRule(
                   property,
                   propertyAccess,
                   attribute,
                   "RequiredItemsIfAttribute",
                   isRequiredWhenConditionMatches: true,
                   out rule);
    }
}
