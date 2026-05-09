using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>PasswordAttribute</c> by calling the attribute's direct value-validation path instead of the runtime
/// whole-object DataAnnotations validator.
/// </summary>
internal sealed class PasswordValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsPasswordAttribute(attribute.AttributeClass)
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
            attributeExpression = "new global::Blueprint.Validation.PasswordAttribute()";
            return true;
        }

        if (attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string errorMessage)
        {
            attributeExpression = $"new global::Blueprint.Validation.PasswordAttribute({CSharpSymbolDisplay.FormatLiteral(errorMessage, quote: true)})";
            return true;
        }

        if (attribute.ConstructorArguments.Length == 2
            && attribute.ConstructorArguments[0].Value is string extendedErrorMessage
            && attribute.ConstructorArguments[1].Value is bool appendInvalidPasswordDescription)
        {
            attributeExpression = $"new global::Blueprint.Validation.PasswordAttribute({CSharpSymbolDisplay.FormatLiteral(extendedErrorMessage, quote: true)}, {(appendInvalidPasswordDescription ? "true" : "false")})";
            return true;
        }

        return false;
    }
}
