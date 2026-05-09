using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>NotInPastAttribute</c> for supported temporal property shapes by comparing directly against the current UTC time.
/// </summary>
internal sealed class NotInPastValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        return ValidationSymbolHelpers.IsNotInPastAttribute(attribute.AttributeClass)
               && TemporalValidationFastPathStrategyHelpers.TryCreateRule(property, propertyAccess, attribute, "NotInPastAttribute", "<", out rule);
    }
}
