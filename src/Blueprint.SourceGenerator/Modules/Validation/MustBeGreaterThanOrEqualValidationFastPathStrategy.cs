using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>MustBeGreaterThanOrEqualAttribute</c> when both properties can be compared directly in generated code
/// without runtime reflection or boxing. Nullable value types are supported when both properties share the same nullable type.
/// </summary>
internal sealed class MustBeGreaterThanOrEqualValidationFastPathStrategy : IValidationFastPathStrategy
{
    /// <inheritdoc />
    public bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.IsMustBeGreaterThanOrEqualAttribute(attribute.AttributeClass)
            || attribute.NamedArguments.Length > 0
            || !TryGetReferencedPropertyAccess(property, propertyAccess, attribute, out var referencedPropertyName, out var referencedPropertyAccess, out var isNullable))
        {
            return false;
        }

        if (isNullable)
        {
            rule = new ValidationFastPathRule(
                $"{propertyAccess}.Value < {referencedPropertyAccess}.Value",
                $"new global::Blueprint.Validation.MustBeGreaterThanOrEqualAttribute(\"{referencedPropertyName}\")",
                GuardCondition: $"{propertyAccess} is not null && {referencedPropertyAccess} is not null");
        }
        else
        {
            rule = new ValidationFastPathRule(
                $"{propertyAccess} < {referencedPropertyAccess}",
                $"new global::Blueprint.Validation.MustBeGreaterThanOrEqualAttribute(\"{referencedPropertyName}\")");
        }

        return true;
    }

    private static bool TryGetReferencedPropertyAccess(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out string referencedPropertyName,
        out string referencedPropertyAccess,
        out bool isNullable)
    {
        referencedPropertyName = string.Empty;
        referencedPropertyAccess = string.Empty;
        isNullable = false;

        var otherPropertyName = ValidationSymbolHelpers.GetReferencedPropertyName(attribute);
        if (string.IsNullOrWhiteSpace(otherPropertyName))
        {
            return false;
        }

        var containingType = property.ContainingType;
        var referencedProperty = ValidationSymbolHelpers.GetPublicInstanceProperties(containingType)
            .FirstOrDefault(candidate => candidate.Name == otherPropertyName);
        if (referencedProperty is null
            || !SupportsDirectComparison(property.Type, referencedProperty.Type, out isNullable))
        {
            return false;
        }

        referencedPropertyName = referencedProperty.Name;

        var propertyAccessPrefix = propertyAccess[..(propertyAccess.LastIndexOf('.') + 1)];
        referencedPropertyAccess = propertyAccessPrefix + ValidationSymbolHelpers.EscapeIdentifier(referencedProperty.Name);
        return true;
    }

    private static bool SupportsDirectComparison(ITypeSymbol propertyType, ITypeSymbol referencedPropertyType, out bool isNullable)
    {
        isNullable = false;

        if (!SymbolEqualityComparer.Default.Equals(propertyType, referencedPropertyType))
        {
            return false;
        }

        if (ValidationSymbolHelpers.IsNullableValueType(propertyType)
            && ValidationSymbolHelpers.IsNullableValueType(referencedPropertyType))
        {
            isNullable = true;
            var underlying = ValidationSymbolHelpers.GetNullableValueTypeArgument(propertyType);
            return underlying is not null && IsComparableNumericOrDateTime(underlying);
        }

        if (ValidationSymbolHelpers.IsNullableValueType(propertyType)
            || ValidationSymbolHelpers.IsNullableValueType(referencedPropertyType))
        {
            return false;
        }

        return IsComparableNumericOrDateTime(propertyType);
    }

    private static bool IsComparableNumericOrDateTime(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_SByte
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
            or SpecialType.System_DateTime
            || ValidationSymbolHelpers.IsDateTimeOffsetType(type);
    }
}
