using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>RequiredIfAttribute</c> by delegating to the shared conditional-required lowering helper.
/// </summary>
internal sealed class RequiredIfValidationFastPathStrategy : IValidationFastPathStrategy
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
