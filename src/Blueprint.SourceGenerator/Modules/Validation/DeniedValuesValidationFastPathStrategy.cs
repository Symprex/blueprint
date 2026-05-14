using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>DeniedValuesAttribute</c> (.NET 8) when all constructor arguments are compile-time literal values of
/// a type that can be safely lowered to generated equality checks, avoiding the runtime reflection path.
/// </summary>
/// <remarks>
/// The generated code checks whether the property value IS in the denied set. When any denied value matches, validation
/// fails. If any constructor argument is null, an array, or an unsupported type the strategy declines and the property
/// falls back to runtime validation.
/// </remarks>
internal sealed class DeniedValuesValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!IsDeniedValuesAttribute(attribute.AttributeClass)
            || attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array
            || attribute.NamedArguments.Length > 0)
        {
            return false;
        }

        var values = attribute.ConstructorArguments[0].Values;
        if (values.IsDefaultOrEmpty || !TryRenderAllLiterals(values, out var literals, out var attributeCtorArgs))
        {
            return false;
        }

        // Build: prop == literal1 || prop == literal2 || ...
        // DeniedValuesAttribute.IsValid(null) returns true, so null values must bypass the check.
        var failureCondition = values.Length == 1
            ? $"{propertyAccess} == {literals[0]}"
            : $"({string.Join(" || ", literals.Select(literal => $"{propertyAccess} == {literal}"))})";

        rule = new ValidationFastPathRule(
            failureCondition,
            $"new global::System.ComponentModel.DataAnnotations.DeniedValuesAttribute({attributeCtorArgs})",
            GuardCondition: $"{propertyAccess} is not null");
        return true;
    }

    private static bool TryRenderAllLiterals(
        ImmutableArray<TypedConstant> values,
        [NotNullWhen(true)] out List<string>? literals,
        [NotNullWhen(true)] out string? attributeCtorArgs)
    {
        literals = null;
        attributeCtorArgs = null;

        var renderedLiterals = new List<string>(values.Length);
        var ctorArgParts = new List<string>(values.Length);

        foreach (var value in values)
        {
            if (value.IsNull || value.Kind is TypedConstantKind.Array or TypedConstantKind.Error
                || !TryRenderLiteral(value, out var literal, out var ctorArg))
            {
                return false;
            }

            renderedLiterals.Add(literal);
            ctorArgParts.Add(ctorArg);
        }

        literals = renderedLiterals;
        attributeCtorArgs = string.Join(", ", ctorArgParts);
        return true;
    }

    private static bool TryRenderLiteral(TypedConstant value, [NotNullWhen(true)] out string? literal, [NotNullWhen(true)] out string? ctorArg)
    {
        literal = null;
        ctorArg = null;

        switch (value.Value)
        {
            case string stringValue:
                literal = CSharpSymbolDisplay.FormatLiteral(stringValue, quote: true);
                ctorArg = literal;
                return true;

            case bool boolValue:
                literal = boolValue ? "true" : "false";
                ctorArg = literal;
                return true;

            case int intValue:
                literal = intValue.ToString(CultureInfo.InvariantCulture);
                ctorArg = literal;
                return true;

            case long longValue:
                literal = longValue.ToString(CultureInfo.InvariantCulture) + "L";
                ctorArg = literal;
                return true;

            case double doubleValue:
                literal = doubleValue.ToString("R", CultureInfo.InvariantCulture);
                ctorArg = literal;
                return true;

            default:
                return false;
        }
    }

    private static bool IsDeniedValuesAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "DeniedValuesAttribute"
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
    }
}
