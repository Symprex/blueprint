using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Centralises Roslyn attribute and property-shape checks used by validation planning so all validation contributors make
/// the same semantic decisions.
/// </summary>
internal static class ValidationSymbolHelpers
{
    /// <summary>
    /// Returns the public instance properties that participate in generated operation validation.
    /// </summary>
    /// <param name="operationType">The operation type to inspect.</param>
    /// <returns>The public instance properties declared on the operation type.</returns>
    public static IEnumerable<IPropertySymbol> GetPublicInstanceProperties(INamedTypeSymbol operationType)
    {
        return operationType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .Where(static property => property is { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public });
    }

    /// <summary>
    /// Determines whether validation must fall back to whole-object runtime evaluation because the type itself carries
    /// validation behaviour that property-level generation cannot safely reproduce.
    /// </summary>
    /// <param name="operationType">The operation type to inspect.</param>
    /// <returns><see langword="true"/> when whole-object runtime validation is required.</returns>
    public static bool RequiresWholeObjectFallback(INamedTypeSymbol operationType)
    {
        return HasValidationAttribute(operationType)
               || operationType.AllInterfaces.Any(static interfaceSymbol => interfaceSymbol.ToDisplayString() == "System.ComponentModel.DataAnnotations.IValidatableObject");
    }

    /// <summary>
    /// Determines whether the supplied symbol has any validation-related attributes.
    /// </summary>
    /// <param name="symbol">The symbol to inspect.</param>
    /// <returns><see langword="true"/> when a validation attribute is present.</returns>
    public static bool HasValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(static attribute => IsValidationAttribute(attribute.AttributeClass));
    }

    /// <summary>
    /// Checks whether an attribute type represents a DataAnnotations validation attribute.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute participates in validation.</returns>
    public static bool IsValidationAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && (attributeClass.ToDisplayString() == "System.ComponentModel.DataAnnotations.ValidationAttribute"
                   || attributeClass.InheritsFrom("System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    /// <summary>
    /// Checks whether an attribute type is <c>CompareAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a compare attribute.</returns>
    public static bool IsCompareAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "CompareAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>GreaterThanPropertyAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a greater-than-property attribute.</returns>
    public static bool IsGreaterThanPropertyAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "GreaterThanPropertyAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>LessThanPropertyAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a less-than-property attribute.</returns>
    public static bool IsLessThanPropertyAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "LessThanPropertyAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>GreaterThanAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a greater-than attribute.</returns>
    public static bool IsGreaterThanAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "GreaterThanAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>LessThanAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a less-than attribute.</returns>
    public static bool IsLessThanAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "LessThanAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>RequiredIfAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a required-if attribute.</returns>
    public static bool IsRequiredIfAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RequiredIfAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>RequiredIfNotAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a required-if-not attribute.</returns>
    public static bool IsRequiredIfNotAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RequiredIfNotAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>NotAllowedIfAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a not-allowed-if attribute.</returns>
    public static bool IsNotAllowedIfAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "NotAllowedIfAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    /// <summary>
    /// Checks whether an attribute influences the display name used in validation messages, which forces runtime fallback.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute changes the display name used for validation.</returns>
    public static bool IsDisplayNameAffectingAttribute(INamedTypeSymbol? attributeClass)
    {
        if (attributeClass is null)
        {
            return false;
        }

        var namespaceName = attributeClass.ContainingNamespace.ToDisplayString();

        return (attributeClass.Name == "DisplayAttribute" && namespaceName == "System.ComponentModel.DataAnnotations")
               || (attributeClass.Name == "DisplayNameAttribute" && namespaceName == "System.ComponentModel");
    }

    /// <summary>
    /// Checks whether an attribute type is <c>RequiredAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a required attribute.</returns>
    public static bool IsRequiredAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RequiredAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether an attribute behaves like a required constraint and therefore should run before other rules.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is required-like.</returns>
    public static bool IsRequiredLikeAttribute(INamedTypeSymbol? attributeClass)
    {
        return IsRequiredAttribute(attributeClass)
               || IsRequiredIfAttribute(attributeClass)
               || IsRequiredIfNotAttribute(attributeClass);
    }

    /// <summary>
    /// Checks whether an attribute type is <c>MaxLengthAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a max-length attribute.</returns>
    public static bool IsMaxLengthAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "MaxLengthAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether an attribute type is <c>MinLengthAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a min-length attribute.</returns>
    public static bool IsMinLengthAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "MinLengthAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether an attribute type is <c>StringLengthAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a string-length attribute.</returns>
    public static bool IsStringLengthAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "StringLengthAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether an attribute type is <c>EmailAddressAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is an email-address attribute.</returns>
    public static bool IsEmailAddressAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "EmailAddressAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether an attribute type is <c>RangeAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a range attribute.</returns>
    public static bool IsRangeAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RangeAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether an attribute type is <c>RegularExpressionAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a regular-expression attribute.</returns>
    public static bool IsRegularExpressionAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RegularExpressionAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the attribute class is <c>System.ComponentModel.DataAnnotations.PhoneAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type symbol to inspect.</param>
    /// <returns><see langword="true"/> when the symbol represents <c>PhoneAttribute</c>.</returns>
    public static bool IsPhoneAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "PhoneAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the attribute class is <c>System.ComponentModel.DataAnnotations.UrlAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type symbol to inspect.</param>
    /// <returns><see langword="true"/> when the symbol represents <c>UrlAttribute</c>.</returns>
    public static bool IsUrlAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "UrlAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    /// <summary>
    /// Checks whether a type is <see cref="Nullable{T}" />.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> when the type is nullable.</returns>
    public static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType
               && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    /// <summary>
    /// Returns the underlying value type for a nullable value type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The wrapped value type when the supplied type is nullable; otherwise, <see langword="null"/>.</returns>
    public static ITypeSymbol? GetNullableValueTypeArgument(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? namedType.TypeArguments[0]
            : null;
    }

    /// <summary>
    /// Escapes a C# identifier when it collides with a language keyword.
    /// </summary>
    /// <param name="identifier">The identifier to escape.</param>
    /// <returns>The escaped identifier when needed; otherwise, the original identifier.</returns>
    public static string EscapeIdentifier(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
    }

    /// <summary>
    /// Reads the first constructor argument from a property-referencing validation attribute as the referenced property name.
    /// </summary>
    /// <param name="attribute">The attribute to inspect.</param>
    /// <returns>The referenced property name, if one is present.</returns>
    public static string? GetReferencedPropertyName(AttributeData attribute)
    {
        return attribute.ConstructorArguments.FirstOrDefault().Value as string;
    }

    /// <summary>
    /// Locates the syntax site that names a referenced property so diagnostics can point at the most helpful source span.
    /// </summary>
    /// <param name="attribute">The attribute whose constructor argument should be highlighted.</param>
    /// <param name="property">The property that owns the attribute, used as a fallback location.</param>
    /// <returns>The best available location for a referenced-property diagnostic.</returns>
    public static LocationInfo? GetReferencedPropertyLocation(AttributeData attribute, IPropertySymbol property)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attributeSyntax
            && attributeSyntax.ArgumentList is { Arguments.Count: > 0 })
        {
            return LocationInfo.CreateFrom(attributeSyntax.ArgumentList.Arguments[0].Expression);
        }

        return property.Locations.Length > 0 ? LocationInfo.CreateFrom(property.Locations[0]) : null;
    }

    /// <summary>
    /// Determines whether a property type is a complex child object whose own properties carry validation attributes
    /// and therefore should be recursively validated.
    /// </summary>
    /// <param name="propertyType">The type of the property.</param>
    /// <returns><see langword="true"/> when the type is a validatable complex object.</returns>
    public static bool IsValidatableComplexType(ITypeSymbol propertyType)
    {
        if (propertyType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        // Exclude primitives, enums, strings, and well-known framework types
        if (namedType.SpecialType != SpecialType.None
            || namedType.TypeKind == TypeKind.Enum
            || namedType.IsValueType)
        {
            return false;
        }

        var fullName = namedType.ToDisplayString();
        if (fullName is "string" or "System.String" or "object" or "System.Object")
        {
            return false;
        }

        // Check if any public instance property on the type has a validation attribute
        return GetPublicInstanceProperties(namedType)
            .Any(static p => p.GetAttributes().Any(static a => IsValidationAttribute(a.AttributeClass)));
    }

    /// <summary>
    /// Tries to extract the element type from a list/collection property if the elements are validatable complex types.
    /// </summary>
    /// <param name="propertyType">The type of the property.</param>
    /// <param name="elementType">The element type if found and validatable.</param>
    /// <returns><see langword="true"/> when the property is a list of validatable complex objects.</returns>
    public static bool TryGetValidatableListElementType(ITypeSymbol propertyType, out INamedTypeSymbol? elementType)
    {
        elementType = null;

        if (propertyType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        // Check IReadOnlyList<T>, IList<T>, List<T>, IEnumerable<T>, ICollection<T>, IReadOnlyCollection<T>
        var candidate = GetGenericListElementType(namedType)
                        ?? namedType.AllInterfaces
                            .Select(static i => GetGenericListElementType(i))
                            .FirstOrDefault(static t => t is not null);

        if (candidate is not null && IsValidatableComplexType(candidate))
        {
            elementType = candidate as INamedTypeSymbol;
            return elementType is not null;
        }

        return false;
    }

    private static ITypeSymbol? GetGenericListElementType(INamedTypeSymbol type)
    {
        if (!type.IsGenericType || type.TypeArguments.Length != 1)
        {
            return null;
        }

        var definition = type.ConstructedFrom.ToDisplayString();
        if (definition is "System.Collections.Generic.IReadOnlyList<T>"
            or "System.Collections.Generic.IList<T>"
            or "System.Collections.Generic.List<T>"
            or "System.Collections.Generic.IEnumerable<T>"
            or "System.Collections.Generic.ICollection<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>")
        {
            return type.TypeArguments[0];
        }

        return null;
    }
}
