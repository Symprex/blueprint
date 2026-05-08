using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>UrlAttribute</c> when the target property is a simple string and the
/// attribute uses the default constructor with no named arguments, emitting a direct
/// <c>new UrlAttribute().IsValid(value)</c> check that avoids the per-object validation runtime path.
/// </summary>
internal sealed class UrlValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsUrlAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || attribute.NamedArguments.Length > 0
            || attribute.ConstructorArguments.Length > 0)
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"new global::System.ComponentModel.DataAnnotations.UrlAttribute().IsValid({propertyAccess}) == false",
            "new global::System.ComponentModel.DataAnnotations.UrlAttribute()",
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }
}
