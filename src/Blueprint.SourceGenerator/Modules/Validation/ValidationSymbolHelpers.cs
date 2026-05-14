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
               || ImplementsIValidatableObject(operationType);
    }

    /// <summary>
    /// Checks whether the type implements <c>System.ComponentModel.DataAnnotations.IValidatableObject</c>
    /// by comparing <see cref="ISymbol.MetadataName"/> first (cheap) then verifying the namespace only
    /// when the name matches — avoids the full display-string cost per interface.
    /// </summary>
    /// <param name="type">The type to inspect for <c>IValidatableObject</c> implementation.</param>
    /// <returns><see langword="true"/> when the type implements <c>IValidatableObject</c>.</returns>
    public static bool ImplementsIValidatableObject(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(static interfaceSymbol =>
            interfaceSymbol.MetadataName == "IValidatableObject"
            && interfaceSymbol.IsInNamespace("System", "ComponentModel", "DataAnnotations"));
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
    /// Uses <see cref="ISymbol.MetadataName"/> for the initial check (cheap) before falling back
    /// to the inheritance walk.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute participates in validation.</returns>
    public static bool IsValidationAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && (IsValidationAttributeExact(attributeClass)
                   || attributeClass.InheritsFrom("System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    private static bool IsValidationAttributeExact(INamedTypeSymbol type)
    {
        return type.MetadataName == "ValidationAttribute"
               && type.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && attributeClass.IsInNamespace("Blueprint", "Validation");
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
               && attributeClass.IsInNamespace("Blueprint", "Validation");
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
               && attributeClass.IsInNamespace("Blueprint", "Validation");
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
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>MustBeTrueAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a must-be-true attribute.</returns>
    public static bool IsMustBeTrueAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "MustBeTrueAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>InFutureAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is an in-future attribute.</returns>
    public static bool IsInFutureAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "InFutureAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>InPastAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is an in-past attribute.</returns>
    public static bool IsInPastAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "InPastAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>NotInFutureAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a not-in-future attribute.</returns>
    public static bool IsNotInFutureAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "NotInFutureAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>NotInPastAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a not-in-past attribute.</returns>
    public static bool IsNotInPastAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "NotInPastAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>NotEmptyListAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a not-empty-list attribute.</returns>
    public static bool IsNotEmptyListAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "NotEmptyListAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>MaxItemsListAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a max-items-list attribute.</returns>
    public static bool IsMaxItemsListAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "MaxItemsListAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>GreaterThanOrEqualToAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a greater-than-or-equal-to attribute.</returns>
    public static bool IsGreaterThanOrEqualToAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "GreaterThanOrEqualToAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>LessThanOrEqualToAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a less-than-or-equal-to attribute.</returns>
    public static bool IsLessThanOrEqualToAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "LessThanOrEqualToAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>MustBeGreaterThanOrEqualAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a must-be-greater-than-or-equal attribute.</returns>
    public static bool IsMustBeGreaterThanOrEqualAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "MustBeGreaterThanOrEqualAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>PasswordAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a password attribute.</returns>
    public static bool IsPasswordAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "PasswordAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>ResourceKeyAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a resource-key attribute.</returns>
    public static bool IsResourceKeyAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "ResourceKeyAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
    }

    /// <summary>
    /// Checks whether an attribute type is Blueprint's <c>UkPostcodeAttribute</c>.
    /// </summary>
    /// <param name="attributeClass">The attribute type to inspect.</param>
    /// <returns><see langword="true"/> when the attribute is a UK-postcode attribute.</returns>
    public static bool IsUkPostcodeAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "UkPostcodeAttribute"
               && attributeClass.IsInNamespace("Blueprint", "Validation");
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
               && attributeClass.IsInNamespace("Blueprint", "Validation");
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
               && attributeClass.IsInNamespace("Blueprint", "Validation");
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
               && attributeClass.IsInNamespace("Blueprint", "Validation");
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

        var ns = attributeClass.ContainingNamespace;

        return (attributeClass.Name == "DisplayAttribute" && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations"))
               || (attributeClass.Name == "DisplayNameAttribute" && ns is { Name: "ComponentModel", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } });
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && (attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations") || attributeClass.IsInNamespace("Blueprint", "Validation"));
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations");
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
               && (attributeClass.IsInNamespace("System", "ComponentModel", "DataAnnotations") || attributeClass.IsInNamespace("Blueprint", "Validation"));
    }

    /// <summary>
    /// Returns <see langword="true"/> when the supplied type is <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> when the type is <see cref="DateTimeOffset"/>.</returns>
    public static bool IsDateTimeOffsetType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType
               && namedType.Name == nameof(DateTimeOffset)
               && namedType.IsInRootNamespace("System");
    }

    /// <summary>
    /// Tries to build a count expression for a collection-like property shape that can be checked in generated code.
    /// </summary>
    /// <param name="type">The property type to inspect.</param>
    /// <param name="propertyAccess">The generated expression used to read the property value.</param>
    /// <param name="countExpression">Receives the generated count expression when supported.</param>
    /// <returns><see langword="true"/> when the property exposes a deterministic count.</returns>
    public static bool TryGetCollectionCountExpression(ITypeSymbol type, string propertyAccess, out string? countExpression)
    {
        if (type is IArrayTypeSymbol)
        {
            countExpression = propertyAccess + ".Length";
            return true;
        }

        if (HasAccessibleIntCountProperty(type))
        {
            countExpression = propertyAccess + ".Count";
            return true;
        }

        if (type is INamedTypeSymbol namedType
            && (IsICollection(namedType)
                || namedType.AllInterfaces.Any(static candidate =>
                    candidate.MetadataName == "ICollection"
                    && candidate.IsInNamespace("System", "Collections"))))
        {
            countExpression = $"((global::System.Collections.ICollection){propertyAccess}).Count";
            return true;
        }

        countExpression = null;
        return false;
    }

    private static bool IsICollection(INamedTypeSymbol type)
    {
        return type.MetadataName == "ICollection"
               && type.IsInNamespace("System", "Collections");
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

    private static bool HasAccessibleIntCountProperty(ITypeSymbol type)
    {
        return type.GetMembers("Count")
            .OfType<IPropertySymbol>()
            .Any(static property => property is { Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public }
                                    && property.Type.SpecialType == SpecialType.System_Int32);
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
