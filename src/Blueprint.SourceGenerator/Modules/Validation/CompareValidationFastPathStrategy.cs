using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>System.ComponentModel.DataAnnotations.CompareAttribute</c> when both properties have the same type
/// and can be compared directly in generated code without runtime reflection.
/// </summary>
internal sealed class CompareValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsCompareAttribute(attribute.AttributeClass)
            || !TryGetReferencedPropertyAccess(property, propertyAccess, attribute, out var referencedPropertyName, out var referencedPropertyAccess))
        {
            return false;
        }

        // CompareAttribute uses Object.Equals for comparison, matching the framework's runtime behaviour.
        rule = new ValidationFastPathRule(
            $"!object.Equals({propertyAccess}, {referencedPropertyAccess})",
            $"new global::System.ComponentModel.DataAnnotations.CompareAttribute(\"{referencedPropertyName}\")",
            CachedAttributeTypeName: "System.ComponentModel.DataAnnotations.CompareAttribute");
        return true;
    }

    private static bool TryGetReferencedPropertyAccess(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out string referencedPropertyName,
        out string referencedPropertyAccess)
    {
        referencedPropertyName = string.Empty;
        referencedPropertyAccess = string.Empty;

        var otherPropertyName = ValidationSymbolHelpers.GetReferencedPropertyName(attribute);
        if (string.IsNullOrWhiteSpace(otherPropertyName))
        {
            return false;
        }

        var containingType = property.ContainingType;
        var referencedProperty = ValidationSymbolHelpers.GetPublicInstanceProperties(containingType)
            .FirstOrDefault(candidate => candidate.Name == otherPropertyName);
        if (referencedProperty is null)
        {
            return false;
        }

        // Both properties must have the same type for a meaningful equality comparison.
        if (!SymbolEqualityComparer.Default.Equals(property.Type, referencedProperty.Type))
        {
            return false;
        }

        // Nullable value types fall back to runtime because the generated != comparison
        // would not match the framework's Object.Equals semantics for lifted nullable operators.
        if (ValidationSymbolHelpers.IsNullableValueType(property.Type))
        {
            return false;
        }

        referencedPropertyName = referencedProperty.Name;

        var propertyAccessPrefix = propertyAccess[..(propertyAccess.LastIndexOf('.') + 1)];
        referencedPropertyAccess = propertyAccessPrefix + ValidationSymbolHelpers.EscapeIdentifier(referencedProperty.Name);
        return true;
    }
}
