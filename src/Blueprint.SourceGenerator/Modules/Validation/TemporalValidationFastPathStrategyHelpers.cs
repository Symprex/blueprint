using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Shared helpers for date-based validation attributes that compare <see cref="DateTime"/> or <see cref="DateTimeOffset"/>
/// values against the current UTC clock in generated code.
/// </summary>
internal static class TemporalValidationFastPathStrategyHelpers
{
    /// <summary>
    /// Builds a <see cref="ValidationFastPathRule"/> for a temporal attribute that compares a property value
    /// against the current UTC time using the specified failure operator.
    /// </summary>
    /// <param name="property">The property symbol being validated.</param>
    /// <param name="propertyAccess">The C# expression used to access the property value.</param>
    /// <param name="attribute">The attribute data to inspect for constructor arguments.</param>
    /// <param name="attributeName">The simple type name of the validation attribute (e.g. <c>InPastAttribute</c>).</param>
    /// <param name="failureOperator">The comparison operator that constitutes a validation failure (e.g. <c>&gt;=</c>).</param>
    /// <param name="rule">When successful, the constructed fast-path rule; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a rule was produced; <see langword="false"/> if the property shape is unsupported.</returns>
    public static bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        string attributeName,
        string failureOperator,
        [NotNullWhen(true)] out ValidationFastPathRule? rule)
    {
        rule = null;

        if (!TryGetTemporalOperands(property.Type, propertyAccess, out var valueAccess, out var nowExpression, out var guardCondition)
            || !TryGetAttributeExpression(attribute, attributeName, out var attributeExpression))
        {
            return false;
        }

        rule = new ValidationFastPathRule(
            $"{valueAccess} {failureOperator} {nowExpression}",
            attributeExpression,
            GuardCondition: guardCondition);
        return true;
    }

    private static bool TryGetTemporalOperands(
        ITypeSymbol propertyType,
        string propertyAccess,
        [NotNullWhen(true)] out string? valueAccess,
        [NotNullWhen(true)] out string? nowExpression,
        out string? guardCondition)
    {
        var isNullable = ValidationSymbolHelpers.IsNullableValueType(propertyType);
        var underlyingType = isNullable
            ? ValidationSymbolHelpers.GetNullableValueTypeArgument(propertyType)
            : propertyType;

        if (underlyingType is null)
        {
            valueAccess = null;
            nowExpression = null;
            guardCondition = null;
            return false;
        }

        if (underlyingType.SpecialType == SpecialType.System_DateTime)
        {
            valueAccess = isNullable ? $"{propertyAccess}.Value" : propertyAccess;
            nowExpression = "global::System.DateTime.UtcNow";
            guardCondition = isNullable ? $"{propertyAccess} is not null" : null;
            return true;
        }

        if (ValidationSymbolHelpers.IsDateTimeOffsetType(underlyingType))
        {
            valueAccess = isNullable ? $"{propertyAccess}.Value" : propertyAccess;
            nowExpression = "global::System.DateTimeOffset.UtcNow";
            guardCondition = isNullable ? $"{propertyAccess} is not null" : null;
            return true;
        }

        valueAccess = null;
        nowExpression = null;
        guardCondition = null;
        return false;
    }

    private static bool TryGetAttributeExpression(AttributeData attribute, string attributeName, [NotNullWhen(true)] out string? attributeExpression)
    {
        attributeExpression = null;

        if (attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length == 0)
        {
            attributeExpression = $"new global::Blueprint.Validation.{attributeName}()";
            return true;
        }

        if (attribute.ConstructorArguments.Length == 1
            && attribute.ConstructorArguments[0].Value is string errorMessage)
        {
            attributeExpression = $"new global::Blueprint.Validation.{attributeName}({CSharpSymbolDisplay.FormatLiteral(errorMessage, quote: true)})";
            return true;
        }

        return false;
    }
}
