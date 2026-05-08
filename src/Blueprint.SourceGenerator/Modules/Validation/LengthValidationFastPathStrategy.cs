using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>LengthAttribute</c> (.NET 8) when the target property is a string and both constructor arguments are
/// integer literals, emitting a direct length bounds check that avoids the runtime reflection path.
/// </summary>
/// <remarks>
/// <c>LengthAttribute(int minimumLength, int maximumLength)</c> validates that a string's length falls within
/// [minimumLength, maximumLength]. For strings this generates <c>prop.Length &lt; min || prop.Length &gt; max</c>
/// after a null guard (null values are valid per the convention that <c>Required</c> handles nullability).
/// </remarks>
internal sealed class LengthValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!IsLengthAttribute(attribute.AttributeClass)
            || attribute.ConstructorArguments.Length != 2
            || attribute.ConstructorArguments[0].Value is not int minLength
            || attribute.ConstructorArguments[1].Value is not int maxLength
            || attribute.NamedArguments.Length > 0
            || property.Type.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        var minLiteral = minLength.ToString(CultureInfo.InvariantCulture);
        var maxLiteral = maxLength.ToString(CultureInfo.InvariantCulture);

        rule = new ValidationFastPathRule(
            $"{propertyAccess}.Length < {minLiteral} || {propertyAccess}.Length > {maxLiteral}",
            $"new global::System.ComponentModel.DataAnnotations.LengthAttribute({minLiteral}, {maxLiteral})",
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }

    private static bool IsLengthAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "LengthAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }
}
