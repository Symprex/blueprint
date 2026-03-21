using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Emits eager DataAnnotations validation through the shared pipeline contributor seam.
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
        if (!RequiresValidation(context.Operation.TypeSymbol))
        {
            return;
        }

        context.AddRenderStep(PipelineStage.Validation, RenderValidation);
    }

    private static bool RequiresValidation(INamedTypeSymbol operationType)
    {
        return HasValidationAttribute(operationType)
               || operationType.AllInterfaces.Any(i => i.ToDisplayString() == "System.ComponentModel.DataAnnotations.IValidatableObject")
               || operationType.GetAllMembers()
                    .OfType<IPropertySymbol>()
                    .Any(property => property is { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public }
                                     && HasValidationAttribute(property));
    }

    private static void ValidateCompareAttributes(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var operationProperties = operationType.GetAllMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property is { IsStatic: false, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public })
            .ToArray();

        foreach (var property in operationProperties)
        {
            foreach (var compareAttribute in property.GetAttributes().Where(attribute => IsCompareAttribute(attribute.AttributeClass)))
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
        return symbol.GetAttributes().Any(attribute => IsValidationAttribute(attribute.AttributeClass));
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

    private static void RenderValidation(PipelineRenderContext context)
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
            branch => branch.Throw("new global::Blueprint.Validation.ValidationException(validationResults)"));
        context.WriteBlankLine();
    }
}
