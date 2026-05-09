using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>InFutureAttribute</c> for supported temporal property shapes by comparing directly against the current UTC time.
/// </summary>
internal sealed class InFutureValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        return ValidationSymbolHelpers.IsInFutureAttribute(attribute.AttributeClass)
               && TemporalValidationFastPathStrategyHelpers.TryCreateRule(property, propertyAccess, attribute, "InFutureAttribute", "<=", out rule);
    }
}
