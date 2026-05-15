using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Handles <c>EmailAddressAttribute</c> when the target property is a simple
/// string and the attribute uses the default runtime options.
/// </summary>
internal sealed class EmailAddressValidationFastPathStrategy : IValidationFastPathStrategy
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

        if (!ValidationSymbolHelpers.IsEmailAddressAttribute(attribute.AttributeClass)
            || property.Type.SpecialType != SpecialType.System_String
            || attribute.NamedArguments.Length > 0
            || attribute.ConstructorArguments.Length > 0)
        {
            return false;
        }

        // Use the same attribute type as applied by the consumer for correct error message formatting.
        // Cache the namespace string to avoid calling ToDisplayString() twice.
        var namespaceFqn = attribute.AttributeClass!.ContainingNamespace.ToDisplayString();
        var qualifiedName = $"global::{namespaceFqn}.EmailAddressAttribute";

        rule = new ValidationFastPathRule(
            $"global::Blueprint.Validation.ValidationRuntime.IsValidEmailAddress({propertyAccess}) == false",
            $"new {qualifiedName}()",
            CachedAttributeTypeName: $"{namespaceFqn}.EmailAddressAttribute");
        return true;
    }
}
