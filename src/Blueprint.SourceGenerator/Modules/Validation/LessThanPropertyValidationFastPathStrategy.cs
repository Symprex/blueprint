using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>LessThanPropertyAttribute</c> when both properties can be compared directly in generated code without
/// runtime reflection or boxing.
/// </summary>
internal sealed class LessThanPropertyValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsLessThanPropertyAttribute(attribute.AttributeClass)
            || !TryGetReferencedPropertyAccess(property, propertyAccess, attribute, out var referencedPropertyName, out var referencedPropertyAccess))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{propertyAccess} >= {referencedPropertyAccess}",
            $"new global::Blueprint.Validation.LessThanPropertyAttribute(\"{referencedPropertyName}\")");
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
        if (referencedProperty is null
            || !SupportsDirectComparison(property.Type, referencedProperty.Type))
        {
            return false;
        }

        referencedPropertyName = referencedProperty.Name;

        var propertyAccessPrefix = propertyAccess[..(propertyAccess.LastIndexOf('.') + 1)];
        referencedPropertyAccess = propertyAccessPrefix + ValidationSymbolHelpers.EscapeIdentifier(referencedProperty.Name);
        return true;
    }

    private static bool SupportsDirectComparison(ITypeSymbol propertyType, ITypeSymbol referencedPropertyType)
    {
        if (!SymbolEqualityComparer.Default.Equals(propertyType, referencedPropertyType)
            || ValidationSymbolHelpers.IsNullableValueType(propertyType)
            || ValidationSymbolHelpers.IsNullableValueType(referencedPropertyType))
        {
            return false;
        }

        return propertyType.SpecialType is SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal
            or SpecialType.System_DateTime;
    }
}
