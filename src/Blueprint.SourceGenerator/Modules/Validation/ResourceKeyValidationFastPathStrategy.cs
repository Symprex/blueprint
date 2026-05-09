using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>ResourceKeyAttribute</c> by calling the attribute's direct value-validation path instead of the runtime
/// whole-object DataAnnotations validator.
/// </summary>
internal sealed class ResourceKeyValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsResourceKeyAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || !TryGetAttributeExpression(attribute, out var attributeExpression))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{attributeExpression}.IsValid({propertyAccess}) == false",
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
            attributeExpression = "new global::Blueprint.Validation.ResourceKeyAttribute()";
            return true;
        }

        if (attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string errorMessage)
        {
            attributeExpression = $"new global::Blueprint.Validation.ResourceKeyAttribute({CSharpSymbolDisplay.FormatLiteral(errorMessage, quote: true)})";
            return true;
        }

        return false;
    }
}
