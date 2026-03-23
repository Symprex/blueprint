using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>NotAllowedIfAttribute</c> when both the referenced condition and the guarded property-state check can be
/// expressed directly in generated code.
/// </summary>
internal sealed class NotAllowedIfValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsNotAllowedIfAttribute(attribute.AttributeClass)
            || !TryGetConditionAndPropertySetExpressions(property, propertyAccess, attribute, out var activeCondition, out var propertySetCondition, out var attributeExpression))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{activeCondition} && {propertySetCondition}",
            $"new global::Blueprint.Validation.NotAllowedIfAttribute({attributeExpression})");
        return true;
    }

    private static bool TryGetConditionAndPropertySetExpressions(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out string activeCondition,
        out string propertySetCondition,
        out string attributeExpression)
    {
        activeCondition = string.Empty;
        propertySetCondition = string.Empty;
        attributeExpression = string.Empty;

        if (!TryGetActiveCondition(property, propertyAccess, attribute, out activeCondition, out var valueExpression))
        {
            return false;
        }

        propertySetCondition = property.Type.IsReferenceType || ValidationSymbolHelpers.IsNullableValueType(property.Type)
            ? $"{propertyAccess} is not null"
            : "true";
        attributeExpression = $"\"{ValidationSymbolHelpers.GetReferencedPropertyName(attribute)}\", {valueExpression}";
        return true;
    }

    private static bool TryGetActiveCondition(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        out string activeCondition,
        out string valueExpression)
    {
        activeCondition = string.Empty;
        valueExpression = string.Empty;

        if (attribute.ConstructorArguments.Length != 2
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        var referencedPropertyName = ValidationSymbolHelpers.GetReferencedPropertyName(attribute);
        if (string.IsNullOrWhiteSpace(referencedPropertyName))
        {
            return false;
        }

        var referencedProperty = ValidationSymbolHelpers.GetPublicInstanceProperties(property.ContainingType)
            .FirstOrDefault(candidate => candidate.Name == referencedPropertyName);
        if (referencedProperty is null
            || attribute.ConstructorArguments[1].IsNull
            || !TryGetComparisonExpression(
                referencedProperty.Type,
                propertyAccess[..(propertyAccess.LastIndexOf('.') + 1)] + ValidationSymbolHelpers.EscapeIdentifier(referencedProperty.Name),
                attribute.ConstructorArguments[1],
                out activeCondition,
                out valueExpression))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetComparisonExpression(
        ITypeSymbol referencedPropertyType,
        string referencedPropertyAccess,
        TypedConstant dependentValue,
        out string activeCondition,
        out string valueExpression)
    {
        activeCondition = string.Empty;
        valueExpression = string.Empty;

        if (dependentValue.Kind is TypedConstantKind.Array or TypedConstantKind.Error
            || dependentValue.Type is null
            || !IsSupportedComparisonType(referencedPropertyType, dependentValue.Type)
            || !TryRenderLiteralExpression(dependentValue, out var literalExpression))
        {
            return false;
        }

        activeCondition = $"{referencedPropertyAccess} == {literalExpression}";
        valueExpression = literalExpression;
        return true;
    }

    private static bool IsSupportedComparisonType(ITypeSymbol referencedPropertyType, ITypeSymbol dependentValueType)
    {
        var comparisonType = ValidationSymbolHelpers.GetNullableValueTypeArgument(referencedPropertyType) ?? referencedPropertyType;
        return SymbolEqualityComparer.Default.Equals(comparisonType, dependentValueType);
    }

    private static bool TryRenderLiteralExpression(TypedConstant value, [NotNullWhen(true)] out string? expression)
    {
        expression = value.Value switch
        {
            bool boolValue => boolValue ? "true" : "false",
            string stringValue => CSharpSymbolDisplay.FormatLiteral(stringValue, quote: true),
            char charValue => CSharpSymbolDisplay.FormatLiteral(charValue, quote: true),
            byte byteValue => byteValue.ToString(CultureInfo.InvariantCulture),
            sbyte sbyteValue => sbyteValue.ToString(CultureInfo.InvariantCulture),
            short shortValue => shortValue.ToString(CultureInfo.InvariantCulture),
            ushort ushortValue => ushortValue.ToString(CultureInfo.InvariantCulture),
            int intValue => intValue.ToString(CultureInfo.InvariantCulture),
            uint uintValue => uintValue.ToString(CultureInfo.InvariantCulture) + "U",
            long longValue => longValue.ToString(CultureInfo.InvariantCulture) + "L",
            ulong ulongValue => ulongValue.ToString(CultureInfo.InvariantCulture) + "UL",
            float floatValue => floatValue.ToString("R", CultureInfo.InvariantCulture) + "F",
            double doubleValue => doubleValue.ToString("R", CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture) + "m",
            _ => null,
        };

        if (expression is not null)
        {
            return true;
        }

        if (value.Type?.TypeKind != TypeKind.Enum)
        {
            return false;
        }

        var enumField = ((INamedTypeSymbol)value.Type).GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(candidate => candidate.HasConstantValue && Equals(candidate.ConstantValue, value.Value));
        if (enumField is null)
        {
            return false;
        }

        expression = $"global::{enumField.ContainingType.ToDisplayString()}.{enumField.Name}";
        return true;
    }
}
