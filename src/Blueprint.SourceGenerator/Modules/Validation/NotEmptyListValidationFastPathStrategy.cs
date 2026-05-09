using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>NotEmptyListAttribute</c> when the property exposes a deterministic item count in generated code.
/// </summary>
internal sealed class NotEmptyListValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsNotEmptyListAttribute(attribute.AttributeClass)
            || !ValidationSymbolHelpers.TryGetCollectionCountExpression(property.Type, propertyAccess, out var countExpression)
            || !TryGetAttributeExpression(attribute, out var attributeExpression))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{countExpression} == 0",
            attributeExpression,
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }

    private static bool TryGetAttributeExpression(AttributeData attribute, [NotNullWhen(true)] out string? attributeExpression)
    {
        attributeExpression = null;

        if (attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length == 0)
        {
            attributeExpression = "new global::Blueprint.Validation.NotEmptyListAttribute()";
            return true;
        }

        if (attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string errorMessage)
        {
            attributeExpression = $"new global::Blueprint.Validation.NotEmptyListAttribute({CSharpSymbolDisplay.FormatLiteral(errorMessage, quote: true)})";
            return true;
        }

        return false;
    }
}
