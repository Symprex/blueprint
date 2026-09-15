using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Shares the conditional-expression lowering used by <c>RequiredIf</c> and <c>RequiredIfNot</c> validation fast paths so
/// both attributes stay behaviourally aligned.
/// </summary>
internal static class ConditionalRequiredValidationFastPathStrategyHelpers
{
    /// <summary>
    /// Tries to create a required-like rule whose activation depends on another property matching a configured value.
    /// </summary>
    /// <param name="property">The property being validated.</param>
    /// <param name="propertyAccess">The generated-code expression used to read the property value.</param>
    /// <param name="attribute">The conditional required attribute instance to translate.</param>
    /// <param name="attributeTypeName">The runtime attribute type name used when emitting validation failures.</param>
    /// <param name="isRequiredWhenConditionMatches">Whether a matching comparison should activate or deactivate the requirement.</param>
    /// <param name="rule">Receives the generated fast-path rule when translation succeeds.</param>
    /// <returns><see langword="true"/> when the attribute can be lowered into a generated fast-path rule.</returns>
    public static bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        string attributeTypeName,
        bool isRequiredWhenConditionMatches,
        [NotNullWhen(true)]
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!TryGetRequiredStateExpressions(property.Type, propertyAccess, out var missingValueCondition, out var presentValueCondition)
            || !TryGetConditionExpressions(property, propertyAccess, attribute, isRequiredWhenConditionMatches, out var activeCondition, out var inactiveCondition, out var attributeExpression))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{activeCondition} && {missingValueCondition}",
            $"new global::Blueprint.Validation.{attributeTypeName}({attributeExpression})",
            $"{inactiveCondition} || {presentValueCondition}");
        return true;
    }

    /// <summary>
    /// Tries to create a rule for the collection-aware conditional-required attributes (<c>RequiredItemsIf</c> /
    /// <c>RequiredItemsIfNot</c>). Unlike <see cref="TryCreateRule"/>, the "missing value" test inspects the
    /// collection's item count and every element so that a <see langword="null"/> or empty collection, or a
    /// collection containing only <see langword="null"/> or whitespace-only elements, fails the requirement when it
    /// is active — matching the runtime attribute's element-aware check. Only collection shapes with a
    /// deterministic count are lowered here; other shapes fall back to the runtime attribute.
    /// </summary>
    /// <param name="property">The collection property being validated.</param>
    /// <param name="propertyAccess">The generated-code expression used to read the property value.</param>
    /// <param name="attribute">The conditional required-items attribute instance to translate.</param>
    /// <param name="attributeTypeName">The runtime attribute type name used when emitting validation failures.</param>
    /// <param name="isRequiredWhenConditionMatches">Whether a matching comparison should activate or deactivate the requirement.</param>
    /// <param name="rule">Receives the generated fast-path rule when translation succeeds.</param>
    /// <returns><see langword="true"/> when the attribute can be lowered into a generated fast-path rule.</returns>
    public static bool TryCreateItemsRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        string attributeTypeName,
        bool isRequiredWhenConditionMatches,
        [NotNullWhen(true)]
        out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!ValidationSymbolHelpers.TryGetCollectionCountExpression(property.Type, propertyAccess, out var countExpression)
            || !TryGetConditionExpressions(property, propertyAccess, attribute, isRequiredWhenConditionMatches, out var activeCondition, out var inactiveCondition, out var attributeExpression))
        {
            return false;
        }

        // A null or empty collection is "missing", and so is a collection whose elements are all null or
        // whitespace-only — matching the runtime attribute's HasItems check. The null and count checks
        // short-circuit so the element scan below only runs against a non-null, non-empty collection.
        // Cast<object>, not Cast<object?>: an explicit nullable-reference-annotated type argument in generated code
        // requires a surrounding #nullable context, which pipeline files don't declare. The null check on each
        // boxed element below makes the annotation unnecessary anyway.
        var qualifyingItemsExpression =
            $"global::System.Linq.Enumerable.All(global::System.Linq.Enumerable.Cast<object>((global::System.Collections.IEnumerable){propertyAccess}), " +
            "static e => e != null && !global::System.String.IsNullOrWhiteSpace(e.ToString()))";
        var missingValueCondition = $"({propertyAccess} is null || {countExpression} == 0 || !{qualifyingItemsExpression})";
        var presentValueCondition = $"({propertyAccess} is not null && {countExpression} > 0 && {qualifyingItemsExpression})";

        rule = new ValidationFastPathRule(
            $"{activeCondition} && {missingValueCondition}",
            $"new global::Blueprint.Validation.{attributeTypeName}({attributeExpression})",
            $"{inactiveCondition} || {presentValueCondition}");
        return true;
    }

    private static bool TryGetConditionExpressions(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        bool isRequiredWhenConditionMatches,
        out string activeCondition,
        out string inactiveCondition,
        out string attributeExpression)
    {
        activeCondition = string.Empty;
        inactiveCondition = string.Empty;
        attributeExpression = string.Empty;

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
        if (referencedProperty is null)
        {
            return false;
        }

        var referencedPropertyAccess = propertyAccess[..(propertyAccess.LastIndexOf('.') + 1)] + ValidationSymbolHelpers.EscapeIdentifier(referencedProperty.Name);
        if (!TryGetComparisonExpressions(referencedProperty.Type, referencedPropertyAccess, attribute.ConstructorArguments[1], isRequiredWhenConditionMatches, out activeCondition, out inactiveCondition, out var valueExpression))
        {
            return false;
        }

        attributeExpression = $"\"{referencedProperty.Name}\", {valueExpression}";
        return true;
    }

    private static bool TryGetComparisonExpressions(
        ITypeSymbol referencedPropertyType,
        string referencedPropertyAccess,
        TypedConstant dependentValue,
        bool isRequiredWhenConditionMatches,
        out string activeCondition,
        out string inactiveCondition,
        out string valueExpression)
    {
        activeCondition = string.Empty;
        inactiveCondition = string.Empty;
        valueExpression = string.Empty;

        if (dependentValue.Kind is TypedConstantKind.Array or TypedConstantKind.Error)
        {
            return false;
        }

        if (dependentValue.IsNull)
        {
            if (referencedPropertyType.IsReferenceType == false
                && ValidationSymbolHelpers.IsNullableValueType(referencedPropertyType) == false)
            {
                return false;
            }

            activeCondition = isRequiredWhenConditionMatches
                ? $"{referencedPropertyAccess} is null"
                : $"{referencedPropertyAccess} is not null";
            inactiveCondition = isRequiredWhenConditionMatches
                ? $"{referencedPropertyAccess} is not null"
                : $"{referencedPropertyAccess} is null";
            valueExpression = "null";
            return true;
        }

        if (dependentValue.Type is null
            || !IsSupportedComparisonType(referencedPropertyType, dependentValue.Type)
            || !TryRenderLiteralExpression(dependentValue, out var literalExpression))
        {
            return false;
        }

        activeCondition = isRequiredWhenConditionMatches
            ? $"{referencedPropertyAccess} == {literalExpression}"
            : $"{referencedPropertyAccess} != {literalExpression}";
        inactiveCondition = isRequiredWhenConditionMatches
            ? $"{referencedPropertyAccess} != {literalExpression}"
            : $"{referencedPropertyAccess} == {literalExpression}";
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
        // Enum values must be handled before the primitive switch because the underlying
        // integer (int, byte, etc.) would match a primitive case, emitting a bare literal
        // like "1" instead of the fully-qualified enum member name.
        if (value.Type?.TypeKind == TypeKind.Enum)
        {
            var enumField = ((INamedTypeSymbol)value.Type).GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(candidate => candidate.HasConstantValue && Equals(candidate.ConstantValue, value.Value));
            if (enumField is null)
            {
                expression = null;
                return false;
            }

            expression = $"global::{enumField.ContainingType.ToDisplayString()}.{enumField.Name}";
            return true;
        }

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

        return expression is not null;
    }

    private static bool TryGetRequiredStateExpressions(
        ITypeSymbol propertyType,
        string propertyAccess,
        out string missingValueCondition,
        out string presentValueCondition)
    {
        if (propertyType.SpecialType == SpecialType.System_String)
        {
            missingValueCondition = $"global::System.String.IsNullOrWhiteSpace({propertyAccess})";
            presentValueCondition = $"!global::System.String.IsNullOrWhiteSpace({propertyAccess})";
            return true;
        }

        if (propertyType.IsReferenceType || ValidationSymbolHelpers.IsNullableValueType(propertyType))
        {
            missingValueCondition = $"{propertyAccess} is null";
            presentValueCondition = $"{propertyAccess} is not null";
            return true;
        }

        missingValueCondition = string.Empty;
        presentValueCondition = string.Empty;
        return false;
    }
}
