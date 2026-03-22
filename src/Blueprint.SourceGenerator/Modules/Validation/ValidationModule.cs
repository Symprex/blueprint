using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Emits eager DataAnnotations validation through the shared pipeline contributor seam, using per-property
/// generated fast paths for supported validation attributes and a shared runtime fallback for unsupported
/// property-level shapes.
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
        ValidateReferencedPropertyAttributes(context);
    }

    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var validationPlan = ValidationPropertyPlanner.Create(context.Operation.TypeSymbol);

        if (validationPlan.RequiresWholeObjectFallback)
        {
            context.AddRenderStep(PipelineStage.Validation, RenderGenericValidation);
            return;
        }

        if (!validationPlan.RequiresValidation)
        {
            return;
        }

        context.AddRenderStep(PipelineStage.Validation, renderContext => RenderPropertyValidation(renderContext, validationPlan.Properties));
    }

    private static void ValidateReferencedPropertyAttributes(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var operationProperties = ValidationSymbolHelpers.GetPublicInstanceProperties(operationType)
            .ToArray();

        foreach (var property in operationProperties)
        {
            AddMissingPropertyDiagnostics(
                context,
                operationType.Name,
                operationProperties,
                property,
                property.GetAttributes().Where(static attribute => ValidationSymbolHelpers.IsCompareAttribute(attribute.AttributeClass)),
                ValidationDiagnosticDescriptors.MissingCompareProperty);

            AddMissingPropertyDiagnostics(
                context,
                operationType.Name,
                operationProperties,
                property,
                property.GetAttributes().Where(static attribute => ValidationSymbolHelpers.IsGreaterThanPropertyAttribute(attribute.AttributeClass)),
                ValidationDiagnosticDescriptors.MissingGreaterThanProperty);

            AddMissingPropertyDiagnostics(
                context,
                operationType.Name,
                operationProperties,
                property,
                property.GetAttributes().Where(static attribute => ValidationSymbolHelpers.IsRequiredIfAttribute(attribute.AttributeClass)),
                ValidationDiagnosticDescriptors.MissingRequiredIfProperty);

            AddMissingPropertyDiagnostics(
                context,
                operationType.Name,
                operationProperties,
                property,
                property.GetAttributes().Where(static attribute => ValidationSymbolHelpers.IsRequiredIfNotAttribute(attribute.AttributeClass)),
                ValidationDiagnosticDescriptors.MissingRequiredIfNotProperty);
        }
    }

    private static void AddMissingPropertyDiagnostics(
        DescriptorContributorContext context,
        string operationTypeName,
        IReadOnlyList<IPropertySymbol> operationProperties,
        IPropertySymbol property,
        IEnumerable<AttributeData> attributes,
        DiagnosticDescriptor diagnosticDescriptor)
    {
        foreach (var attribute in attributes)
        {
            var referencedPropertyName = ValidationSymbolHelpers.GetReferencedPropertyName(attribute);
            if (string.IsNullOrWhiteSpace(referencedPropertyName))
            {
                continue;
            }

            if (operationProperties.Any(candidate => candidate.Name == referencedPropertyName))
            {
                continue;
            }

            context.AddDiagnostic(
                new DiagnosticInfo(
                    diagnosticDescriptor,
                    ValidationSymbolHelpers.GetReferencedPropertyLocation(attribute, property),
                    [
                        referencedPropertyName!,
                        property.Name,
                        operationTypeName,
                    ]));
        }
    }

    private static void RenderPropertyValidation(PipelineRenderContext context, IReadOnlyList<PropertyValidationPlan> properties)
    {
        context.DeclareVariable(
            "validationResults",
            "default(global::System.Collections.Generic.List<global::System.ComponentModel.DataAnnotations.ValidationResult>)");

        foreach (var property in properties)
        {
            switch (property)
            {
                case FastPathPropertyValidationPlan fastPathProperty:
                    RenderFastPathValidation(context, fastPathProperty);
                    break;
                case RuntimeFallbackPropertyValidationPlan runtimeFallbackProperty:
                    RenderRuntimeFallbackValidation(context, runtimeFallbackProperty);
                    break;
            }
        }

        context.If(
            "validationResults is not null",
            static branch => branch.Throw("new global::Blueprint.Validation.ValidationException(validationResults)"));
        context.WriteBlankLine();
    }

    private static void RenderFastPathValidation(PipelineRenderContext context, FastPathPropertyValidationPlan property)
    {
        if (property.RequiredRule is not null)
        {
            RenderValidationFailure(context, property.RequiredRule.FailureCondition, property.RequiredRule, property);
        }

        foreach (var rule in property.OtherRules)
        {
            var failureOperand = WrapConditionOperand(rule.FailureCondition);
            var failureCondition = property.RequiredRule?.SuccessCondition is string successCondition
                ? $"{successCondition} && {failureOperand}"
                : rule.GuardCondition is string guardCondition
                    ? $"{guardCondition} && {failureOperand}"
                    : rule.FailureCondition;
            RenderValidationFailure(context, failureCondition, rule, property);
        }

        static string WrapConditionOperand(string condition)
        {
            if ((condition.StartsWith('(') && condition.EndsWith(')'))
                || (!condition.Contains("&&", StringComparison.Ordinal) && !condition.Contains("||", StringComparison.Ordinal)))
            {
                return condition;
            }

            return $"({condition})";
        }
    }

    private static void RenderRuntimeFallbackValidation(PipelineRenderContext context, RuntimeFallbackPropertyValidationPlan property)
    {
        context.WriteStatement(
            $"global::Blueprint.Validation.ValidationRuntime.ValidateProperty({context.GetContextPropertyExpression("Descriptor")}.GetPropertyAttributes(\"{property.MemberName}\"), {context.GetOperationExpression()}, {property.PropertyAccess}, \"{property.MemberName}\", ref validationResults);");
    }

    private static void RenderValidationFailure(
        PipelineRenderContext context,
        string failureCondition,
        ValidationFastPathRule rule,
        FastPathPropertyValidationPlan property)
    {
        context.If(
            failureCondition,
            branch =>
            {
                branch.Assign(
                    "validationResults",
                    "validationResults ?? new global::System.Collections.Generic.List<global::System.ComponentModel.DataAnnotations.ValidationResult>()");
                var validationAttributeExpression = rule.WrapValidationAttributeExpression
                    ? $"({rule.ValidationAttributeExpression})"
                    : rule.ValidationAttributeExpression;
                branch.WriteStatement(
                    $"validationResults.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult({validationAttributeExpression}.FormatErrorMessage(\"{property.DisplayName}\"), [\"{property.MemberName}\"]));");
            });
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
}
