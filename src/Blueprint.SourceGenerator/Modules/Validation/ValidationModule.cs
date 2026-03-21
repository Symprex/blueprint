using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Emits eager DataAnnotations validation through the shared pipeline contributor seam, using a narrow
/// generated fast path for default <c>RequiredAttribute</c> shapes and the framework fallback for
/// everything that can influence validation behavior or message formatting.
/// </summary>
public sealed class ValidationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    public string ModuleId => nameof(ValidationModule);

    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    public string ContributorId => nameof(ValidationModule);

    public int Order => 0;

    public bool IsUnique => true;

    public IReadOnlyList<string> DependsOn { get; } = [];

    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        ValidateCompareAttributes(context);
    }

    public void ContributeToPipeline(PipelineContributorContext context)
    {
        if (TryGetRequiredFastPath(context.Operation.TypeSymbol, out var requiredProperties))
        {
            context.AddRenderStep(PipelineStage.Validation, renderContext => RenderRequiredValidation(renderContext, requiredProperties));
            return;
        }

        if (!RequiresValidation(context.Operation.TypeSymbol))
        {
            return;
        }

        context.AddRenderStep(PipelineStage.Validation, RenderGenericValidation);
    }

    private static bool RequiresValidation(INamedTypeSymbol operationType)
    {
        return HasValidationAttribute(operationType)
               || operationType.AllInterfaces.Any(static interfaceSymbol => interfaceSymbol.ToDisplayString() == "System.ComponentModel.DataAnnotations.IValidatableObject")
               || operationType.GetAllMembers()
                     .OfType<IPropertySymbol>()
                     .Any(static property => property is { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public }
                                             && HasValidationAttribute(property));
    }

    private static void ValidateCompareAttributes(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var operationProperties = operationType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .Where(static property => property is { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public })
            .ToArray();

        foreach (var property in operationProperties)
        {
            foreach (var compareAttribute in property.GetAttributes().Where(static attribute => IsCompareAttribute(attribute.AttributeClass)))
            {
                var otherPropertyName = compareAttribute.ConstructorArguments.FirstOrDefault().Value as string;
                if (string.IsNullOrWhiteSpace(otherPropertyName))
                {
                    continue;
                }

                if (operationProperties.Any(candidate => candidate.Name == otherPropertyName))
                {
                    continue;
                }

                context.AddDiagnostic(
                    new DiagnosticInfo(
                        ValidationDiagnosticDescriptors.MissingCompareProperty,
                        GetComparePropertyLocation(compareAttribute, property),
                        [
                            otherPropertyName!,
                            property.Name,
                            operationType.Name,
                        ]));
            }
        }
    }

    private static bool HasValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(static attribute => IsValidationAttribute(attribute.AttributeClass));
    }

    private static bool IsValidationAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && (attributeClass.ToDisplayString() == "System.ComponentModel.DataAnnotations.ValidationAttribute"
                   || attributeClass.InheritsFrom("System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    private static bool IsCompareAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "CompareAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    private static LocationInfo? GetComparePropertyLocation(AttributeData compareAttribute, IPropertySymbol property)
    {
        if (compareAttribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attributeSyntax
            && attributeSyntax.ArgumentList is { Arguments.Count: > 0 })
        {
            return LocationInfo.CreateFrom(attributeSyntax.ArgumentList.Arguments[0].Expression);
        }

        return property.Locations.Length > 0 ? LocationInfo.CreateFrom(property.Locations[0]) : null;
    }

    private static bool TryGetRequiredFastPath(INamedTypeSymbol operationType, out IReadOnlyList<RequiredValidationProperty> properties)
    {
        var requiredProperties = new List<RequiredValidationProperty>();

        properties = requiredProperties;

        // Type-level validation and IValidatableObject can introduce rules that the generated fast path
        // cannot prove or preserve, so they always stay on the framework-controlled fallback path.
        if (HasValidationAttribute(operationType)
            || operationType.AllInterfaces.Any(static interfaceSymbol => interfaceSymbol.ToDisplayString() == "System.ComponentModel.DataAnnotations.IValidatableObject"))
        {
            return false;
        }

        foreach (var property in operationType.GetAllMembers()
                     .OfType<IPropertySymbol>()
                     .Where(static property => property is { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public }))
        {
            var validationAttributes = property.GetAttributes()
                .Where(static attribute => IsValidationAttribute(attribute.AttributeClass))
                .ToArray();

            if (validationAttributes.Length == 0)
            {
                continue;
            }

            // Display metadata and non-default Required settings shape the emitted error text. Falling
            // back keeps DataAnnotations authoritative for those cases instead of approximating it.
            if (property.GetAttributes().Any(static attribute => IsDisplayNameAffectingAttribute(attribute.AttributeClass)))
            {
                return false;
            }

            if (validationAttributes.Any(static attribute => !IsRequiredAttribute(attribute.AttributeClass) || !IsSupportedRequiredAttribute(attribute)))
            {
                return false;
            }

            if (TryCreateRequiredValidationProperty(property, out var requiredProperty))
            {
                requiredProperties.Add(requiredProperty);
            }
        }

        return requiredProperties.Count > 0;
    }

    private static bool TryCreateRequiredValidationProperty(IPropertySymbol property, out RequiredValidationProperty requiredProperty)
    {
        var propertyName = property.Name;
        var escapedPropertyName = EscapeIdentifier(property.Name);
        var propertyAccess = $"operation.{escapedPropertyName}";

        if (property.Type.SpecialType == SpecialType.System_String)
        {
            requiredProperty = new RequiredValidationProperty(
                propertyName,
                $"global::System.String.IsNullOrWhiteSpace({propertyAccess})",
                propertyName);
            return true;
        }

        if (property.Type.IsReferenceType || IsNullableValueType(property.Type))
        {
            requiredProperty = new RequiredValidationProperty(
                propertyName,
                $"{propertyAccess} is null",
                propertyName);
            return true;
        }

        requiredProperty = default;
        return false;
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType
               && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool IsRequiredAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null
               && attributeClass.Name == "RequiredAttribute"
               && attributeClass.ContainingNamespace.ToDisplayString() == "System.ComponentModel.DataAnnotations";
    }

    private static bool IsSupportedRequiredAttribute(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "AllowEmptyStrings")
            {
                if (namedArgument.Value.Value is bool allowEmptyStrings && allowEmptyStrings)
                {
                    return false;
                }

                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsDisplayNameAffectingAttribute(INamedTypeSymbol? attributeClass)
    {
        if (attributeClass is null)
        {
            return false;
        }

        var namespaceName = attributeClass.ContainingNamespace.ToDisplayString();

        return (attributeClass.Name == "DisplayAttribute" && namespaceName == "System.ComponentModel.DataAnnotations")
               || (attributeClass.Name == "DisplayNameAttribute" && namespaceName == "System.ComponentModel");
    }

    private static string EscapeIdentifier(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? "@" + identifier : identifier;
    }

    private static void RenderRequiredValidation(PipelineRenderContext context, IReadOnlyList<RequiredValidationProperty> properties)
    {
        context.DeclareVariable(
            "validationResults",
            "default(global::System.Collections.Generic.List<global::System.ComponentModel.DataAnnotations.ValidationResult>)");

        foreach (var property in properties)
        {
            context.If(
                property.Condition,
                branch =>
                {
                    branch.Assign(
                        "validationResults",
                        "validationResults ?? new global::System.Collections.Generic.List<global::System.ComponentModel.DataAnnotations.ValidationResult>()");

                    // The hot path should stay allocation-light, but invalid results still need the same
                    // default RequiredAttribute message formatting that the framework would have produced.
                    branch.WriteStatement(
                        $"validationResults.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult(new global::System.ComponentModel.DataAnnotations.RequiredAttribute().FormatErrorMessage(\"{property.DisplayName}\"), [\"{property.MemberName}\"]));");
                });
        }

        context.If(
            "validationResults is not null",
            static branch => branch.Throw("new global::Blueprint.Validation.ValidationException(validationResults)"));
        context.WriteBlankLine();
    }

    private static void RenderGenericValidation(PipelineRenderContext context)
    {
        context.DeclareVariable(
            "validationResults",
            "new global::System.Collections.Generic.List<global::System.ComponentModel.DataAnnotations.ValidationResult>()");
        context.DeclareVariable(
            "validationContext",
            $"new global::System.ComponentModel.DataAnnotations.ValidationContext({context.GetOperationExpression()})");
        context.DeclareVariable(
            "isValid",
            $"global::System.ComponentModel.DataAnnotations.Validator.TryValidateObject({context.GetOperationExpression()}, validationContext, validationResults, validateAllProperties: true)");
        context.If(
            "isValid == false",
            static branch => branch.Throw("new global::Blueprint.Validation.ValidationException(validationResults)"));
        context.WriteBlankLine();
    }

    private readonly record struct RequiredValidationProperty(
        string MemberName,
        string Condition,
        string DisplayName);
}
