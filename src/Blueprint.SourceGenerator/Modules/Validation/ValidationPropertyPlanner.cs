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
        new RequiredIfValidationFastPathStrategy(),
        new RequiredIfNotValidationFastPathStrategy(),
        new NotAllowedIfValidationFastPathStrategy(),
        new GreaterThanValidationFastPathStrategy(),
        new GreaterThanPropertyValidationFastPathStrategy(),
    ];

    /// <summary>
    /// Builds the validation plan for an operation type by trying each property against the supported fast-path strategies.
    /// </summary>
    /// <param name="operationType">The operation type whose validation attributes should be analysed.</param>
    /// <returns>The validation plan that describes fast-path and runtime-fallback decisions.</returns>
    [MustUseReturnValue]
    public static ValidationPipelinePlan Create(INamedTypeSymbol operationType)
    {
        if (ValidationSymbolHelpers.RequiresWholeObjectFallback(operationType))
        {
            return new ValidationPipelinePlan(RequiresWholeObjectFallback: true, Properties: []);
        }

        var properties = new List<PropertyValidationPlan>();

        foreach (var property in ValidationSymbolHelpers.GetPublicInstanceProperties(operationType))
        {
            var validationAttributes = property.GetAttributes()
                .Where(static attribute => ValidationSymbolHelpers.IsValidationAttribute(attribute.AttributeClass))
                .ToArray();

            if (validationAttributes.Length == 0)
            {
                continue;
            }

            var propertyAccess = $"operation.{ValidationSymbolHelpers.EscapeIdentifier(property.Name)}";
            if (property.GetAttributes().Any(static attribute => ValidationSymbolHelpers.IsDisplayNameAffectingAttribute(attribute.AttributeClass)))
            {
                properties.Add(new RuntimeFallbackPropertyValidationPlan(property.Name, propertyAccess));
                continue;
            }

            if (TryCreateFastPathPlan(property, propertyAccess, validationAttributes, out var fastPathPlan))
            {
                properties.Add(fastPathPlan);
                continue;
            }

            properties.Add(new RuntimeFallbackPropertyValidationPlan(property.Name, propertyAccess));
        }

        return new ValidationPipelinePlan(RequiresWholeObjectFallback: false, Properties: properties);
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
