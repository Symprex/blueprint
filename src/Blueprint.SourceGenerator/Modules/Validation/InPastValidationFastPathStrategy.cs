using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>InPastAttribute</c> for supported temporal property shapes by comparing directly against the current UTC time.
/// </summary>
internal sealed class InPastValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        return ValidationSymbolHelpers.IsInPastAttribute(attribute.AttributeClass)
               && TemporalValidationFastPathStrategyHelpers.TryCreateRule(property, propertyAccess, attribute, "InPastAttribute", ">=", out rule);
    }
}
