using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>MustBeTrueAttribute</c> for boolean properties by lowering the consent check into one direct boolean test.
/// </summary>
internal sealed class MustBeTrueValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsMustBeTrueAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_Boolean
            || !TryGetAttributeExpression(attribute, out var attributeExpression))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{propertyAccess} == false",
            attributeExpression);
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
            attributeExpression = "new global::Blueprint.Validation.MustBeTrueAttribute()";
            return true;
        }

        if (attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string errorMessage)
        {
            attributeExpression = $"new global::Blueprint.Validation.MustBeTrueAttribute({CSharpSymbolDisplay.FormatLiteral(errorMessage, quote: true)})";
            return true;
        }

        return false;
    }
}
