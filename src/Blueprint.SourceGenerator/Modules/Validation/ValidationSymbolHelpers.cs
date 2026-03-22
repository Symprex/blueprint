using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules;

internal static class ValidationSymbolHelpers
{
    public static IEnumerable<IPropertySymbol> GetPublicInstanceProperties(INamedTypeSymbol operationType)
    {
        return operationType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .Where(static property => property is { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public });
    }

    public static bool RequiresWholeObjectFallback(INamedTypeSymbol operationType)
    {
        return HasValidationAttribute(operationType)
               || operationType.AllInterfaces.Any(static interfaceSymbol => interfaceSymbol.ToDisplayString() == "System.ComponentModel.DataAnnotations.IValidatableObject");
    }

    public static bool HasValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(static attribute => IsValidationAttribute(attribute.AttributeClass));
    }

    public static bool IsValidationAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && (attributeClass.ToDisplayString() == "System.ComponentModel.DataAnnotations.ValidationAttribute"
                   || attributeClass.InheritsFrom("System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    public static bool IsCompareAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "CompareAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    public static bool IsGreaterThanPropertyAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "GreaterThanPropertyAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    public static bool IsGreaterThanAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "GreaterThanAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    public static bool IsRequiredIfAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RequiredIfAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    public static bool IsRequiredIfNotAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RequiredIfNotAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

    public static bool IsNotAllowedIfAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "NotAllowedIfAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Validation";
    }

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

    public static bool IsRequiredAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RequiredAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    public static bool IsRequiredLikeAttribute(INamedTypeSymbol? attributeClass)
    {
        return IsRequiredAttribute(attributeClass)
               || IsRequiredIfAttribute(attributeClass)
               || IsRequiredIfNotAttribute(attributeClass);
    }

    public static bool IsMaxLengthAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "MaxLengthAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    public static bool IsMinLengthAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "MinLengthAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    public static bool IsStringLengthAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "StringLengthAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    public static bool IsEmailAddressAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "EmailAddressAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    public static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType
               && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    public static ITypeSymbol? GetNullableValueTypeArgument(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? namedType.TypeArguments[0]
            : null;
    }

    public static string EscapeIdentifier(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
    }

    public static string? GetReferencedPropertyName(AttributeData attribute)
    {
        return attribute.ConstructorArguments.FirstOrDefault().Value as string;
    }

    public static LocationInfo? GetReferencedPropertyLocation(AttributeData attribute, IPropertySymbol property)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attributeSyntax
            && attributeSyntax.ArgumentList is { Arguments.Count: > 0 })
        {
            return LocationInfo.CreateFrom(attributeSyntax.ArgumentList.Arguments[0].Expression);
        }

        return property.Locations.Length > 0 ? LocationInfo.CreateFrom(property.Locations[0]) : null;
    }
}
