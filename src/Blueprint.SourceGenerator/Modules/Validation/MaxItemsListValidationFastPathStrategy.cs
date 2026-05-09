using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>MaxItemsListAttribute</c> when the property exposes a deterministic item count in generated code.
/// </summary>
internal sealed class MaxItemsListValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsMaxItemsListAttribute(attribute.AttributeClass)
            || !ValidationSymbolHelpers.TryGetCollectionCountExpression(property.Type, propertyAccess, out var countExpression)
            || !TryGetAttributeExpression(attribute, out var attributeExpression, out var maximumItems))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{countExpression} > {maximumItems}",
            attributeExpression,
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }

    private static bool TryGetAttributeExpression(AttributeData attribute, [NotNullWhen(true)] out string? attributeExpression, out int maximumItems)
    {
        attributeExpression = null;
        maximumItems = default;

        if (attribute.NamedArguments.Length > 0
            || attribute.ConstructorArguments.Length is < 1 or > 2
            || attribute.ConstructorArguments[0].Value is not int supportedMaximumItems)
        {
            return false;
        }

        maximumItems = supportedMaximumItems;
        var maximumLiteral = supportedMaximumItems.ToString(CultureInfo.InvariantCulture);

        if (attribute.ConstructorArguments.Length == 1)
        {
            attributeExpression = $"new global::Blueprint.Validation.MaxItemsListAttribute({maximumLiteral})";
            return true;
        }

        if (attribute.ConstructorArguments[1].Value is string errorMessage)
        {
            attributeExpression = $"new global::Blueprint.Validation.MaxItemsListAttribute({maximumLiteral}, {CSharpSymbolDisplay.FormatLiteral(errorMessage, quote: true)})";
            return true;
        }

        return false;
    }
}
