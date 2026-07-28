using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>RequiredItemsIfNotAttribute</c> by delegating to the shared conditional-required collection lowering helper.
/// </summary>
internal sealed class RequiredItemsIfNotValidationFastPathStrategy : IValidationFastPathStrategy
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

        return ValidationSymbolHelpers.IsRequiredItemsIfNotAttribute(attribute.AttributeClass)
               && ConditionalRequiredValidationFastPathStrategyHelpers.TryCreateItemsRule(
                   property,
                   propertyAccess,
                   attribute,
                   "RequiredItemsIfNotAttribute",
                   isRequiredWhenConditionMatches: false,
                   out rule);
    }
}
