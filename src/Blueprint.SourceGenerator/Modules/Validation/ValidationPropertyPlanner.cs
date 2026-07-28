using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Decides whether each validation-decorated property can be lowered into generated checks or must fall back to the
/// existing runtime validation path.
/// </summary>
internal static class ValidationPropertyPlanner
{
    private static readonly IValidationFastPathStrategy[] _strategies =
    [
        new RequiredValidationFastPathStrategy(),
        new MaxLengthValidationFastPathStrategy(),
        new MinLengthValidationFastPathStrategy(),
        new StringLengthValidationFastPathStrategy(),
        new EmailAddressValidationFastPathStrategy(),
        new PhoneValidationFastPathStrategy(),
        new UrlValidationFastPathStrategy(),
        new RangeValidationFastPathStrategy(),
        new RegularExpressionValidationFastPathStrategy(),
        new RequiredIfValidationFastPathStrategy(),
        new RequiredIfNotValidationFastPathStrategy(),
        new RequiredItemsIfValidationFastPathStrategy(),
        new RequiredItemsIfNotValidationFastPathStrategy(),
        new NotAllowedIfValidationFastPathStrategy(),
        new GreaterThanValidationFastPathStrategy(),
        new LessThanValidationFastPathStrategy(),
        new MustBeTrueValidationFastPathStrategy(),
        new InFutureValidationFastPathStrategy(),
        new InPastValidationFastPathStrategy(),
        new NotInFutureValidationFastPathStrategy(),
        new NotInPastValidationFastPathStrategy(),
        new NotEmptyListValidationFastPathStrategy(),
        new MaxItemsListValidationFastPathStrategy(),
        new GreaterThanOrEqualToValidationFastPathStrategy(),
        new LessThanOrEqualToValidationFastPathStrategy(),
        new GreaterThanPropertyValidationFastPathStrategy(),
        new LessThanPropertyValidationFastPathStrategy(),
        new MustBeGreaterThanOrEqualValidationFastPathStrategy(),
        new CompareValidationFastPathStrategy(),
        new PasswordValidationFastPathStrategy(),
        new ResourceKeyValidationFastPathStrategy(),
        new UkPostcodeValidationFastPathStrategy(),
        new AllowedValuesValidationFastPathStrategy(),
        new DeniedValuesValidationFastPathStrategy(),
        new LengthValidationFastPathStrategy(),
    ];

    /// <summary>
    /// Builds the validation plan for an operation type by trying each property against the supported fast-path strategies.
    /// </summary>
    /// <param name="operationType">The operation type whose validation attributes should be analysed.</param>
    /// <returns>The validation plan that describes fast-path and runtime-fallback decisions.</returns>
    [MustUseReturnValue]
    public static ValidationPipelinePlan Create(INamedTypeSymbol operationType)
    {
        // Class-level validation attributes (e.g. [CustomValidation] on the type) require full runtime fallback
        // because they may depend on cross-property state that we cannot safely decompose at compile time.
        if (ValidationSymbolHelpers.HasValidationAttribute(operationType))
        {
            return new ValidationPipelinePlan(RequiresWholeObjectFallback: true, ImplementsIValidatableObject: false, Properties: []);
        }

        var implementsIValidatableObject = ValidationSymbolHelpers.ImplementsIValidatableObject(operationType);

        var properties = new List<PropertyValidationPlan>();

        foreach (var property in ValidationSymbolHelpers.GetPublicInstanceProperties(operationType))
        {
            var propertyAccess = $"operation.{ValidationSymbolHelpers.EscapeIdentifier(property.Name)}";

            var allAttributes = property.GetAttributes();

            // Filter validation attributes without LINQ to avoid iterator + array allocation per property
            List<AttributeData>? validationAttributeList = null;
            foreach (var attribute in allAttributes)
            {
                if (ValidationSymbolHelpers.IsValidationAttribute(attribute.AttributeClass))
                {
                    validationAttributeList ??= new List<AttributeData>(4);
                    validationAttributeList.Add(attribute);
                }
            }

            if (validationAttributeList is null)
            {
                // Even without direct attributes, check if the property is a complex child type
                // whose own properties have validation attributes
                if (ValidationSymbolHelpers.TryGetValidatableListElementType(property.Type, out var elementType))
                {
                    properties.Add(new ChildListValidationPlan(
                        property.Name,
                        propertyAccess,
                        elementType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }
                else if (ValidationSymbolHelpers.IsValidatableComplexType(property.Type))
                {
                    properties.Add(new ChildObjectValidationPlan(
                        property.Name,
                        propertyAccess,
                        property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }

                continue;
            }

            if (allAttributes.Any(static attribute => ValidationSymbolHelpers.IsDisplayNameAffectingAttribute(attribute.AttributeClass)))
            {
                properties.Add(new RuntimeFallbackPropertyValidationPlan(property.Name, propertyAccess));
                AppendChildValidationIfApplicable(properties, property, propertyAccess);
                continue;
            }

            if (TryCreateFastPathPlan(property, propertyAccess, validationAttributeList, out var fastPathPlan))
            {
                properties.Add(fastPathPlan);
                AppendChildValidationIfApplicable(properties, property, propertyAccess);
                continue;
            }

            properties.Add(new RuntimeFallbackPropertyValidationPlan(property.Name, propertyAccess));
            AppendChildValidationIfApplicable(properties, property, propertyAccess);
        }

        return new ValidationPipelinePlan(RequiresWholeObjectFallback: false, ImplementsIValidatableObject: implementsIValidatableObject, Properties: properties);
    }

    /// <summary>
    /// When a property has its own validation attributes (e.g. [Required]) AND is also a validatable
    /// collection or complex child type, we need to emit both the property-level validation and the
    /// recursive child validation. This appends the child validation plan entry after the property's
    /// own plan entry.
    /// </summary>
    private static void AppendChildValidationIfApplicable(
        List<PropertyValidationPlan> properties,
        IPropertySymbol property,
        string propertyAccess)
    {
        if (ValidationSymbolHelpers.TryGetValidatableListElementType(property.Type, out var elementType))
        {
            properties.Add(new ChildListValidationPlan(
                property.Name,
                propertyAccess,
                elementType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
        else if (ValidationSymbolHelpers.IsValidatableComplexType(property.Type))
        {
            properties.Add(new ChildObjectValidationPlan(
                property.Name,
                propertyAccess,
                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
    }

    private static bool TryCreateFastPathPlan(
        IPropertySymbol property,
        string propertyAccess,
        IReadOnlyList<AttributeData> validationAttributes,
        out FastPathPropertyValidationPlan plan)
    {
        ValidationFastPathRule? requiredRule = null;
        var otherRules = new List<ValidationFastPathRule>();

        foreach (var attribute in validationAttributes)
        {
            if (!TryCreateRule(property, propertyAccess, attribute, out var rule))
            {
                plan = default!;
                return false;
            }

            if (ValidationSymbolHelpers.IsRequiredLikeAttribute(attribute.AttributeClass))
            {
                if (requiredRule is not null)
                {
                    plan = default!;
                    return false;
                }

                requiredRule = rule;
                continue;
            }

            otherRules.Add(rule);
        }

        plan = new FastPathPropertyValidationPlan(
            property.Name,
            property.Name,
            propertyAccess,
            requiredRule,
            otherRules);
        return requiredRule is not null || otherRules.Count > 0;
    }

    private static bool TryCreateRule(
        IPropertySymbol property,
        string propertyAccess,
        AttributeData attribute,
        [NotNullWhen(true)]
        out ValidationFastPathRule? rule)
    {
        foreach (var strategy in _strategies)
        {
            if (strategy.TryCreateRule(property, propertyAccess, attribute, out var candidateRule))
            {
                rule = candidateRule;
                return true;
            }
        }

        rule = null;
        return false;
    }
}
