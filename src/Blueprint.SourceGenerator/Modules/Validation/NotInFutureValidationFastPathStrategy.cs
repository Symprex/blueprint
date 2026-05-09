using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>NotInFutureAttribute</c> for supported temporal property shapes by comparing directly against the current UTC time.
/// </summary>
internal sealed class NotInFutureValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        return ValidationSymbolHelpers.IsNotInFutureAttribute(attribute.AttributeClass)
               && TemporalValidationFastPathStrategyHelpers.TryCreateRule(property, propertyAccess, attribute, "NotInFutureAttribute", ">", out rule);
    }
}
