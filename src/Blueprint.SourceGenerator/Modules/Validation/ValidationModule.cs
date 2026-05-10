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
    /// <inheritdoc />
    public string ModuleId => nameof(ValidationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ValidationModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        ValidateReferencedPropertyAttributes(context);
    }

    /// <inheritdoc />
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
                property.GetAttributes().Where(static attribute => ValidationSymbolHelpers.IsLessThanPropertyAttribute(attribute.AttributeClass)),
                ValidationDiagnosticDescriptors.MissingLessThanProperty);

            AddMissingPropertyDiagnostics(
                context,
                operationType.Name,
                operationProperties,
                property,
                property.GetAttributes().Where(static attribute => ValidationSymbolHelpers.IsMustBeGreaterThanOrEqualAttribute(attribute.AttributeClass)),
                ValidationDiagnosticDescriptors.MissingMustBeGreaterThanOrEqualProperty);

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

            AddMissingPropertyDiagnostics(
                context,
                operationType.Name,
                operationProperties,
                property,
                property.GetAttributes().Where(static attribute => ValidationSymbolHelpers.IsNotAllowedIfAttribute(attribute.AttributeClass)),
                ValidationDiagnosticDescriptors.MissingNotAllowedIfProperty);

            ValidateConflictingRanges(context, operationType.Name, property);
            ValidateEmptyRegexPattern(context, operationType.Name, property);
            ValidateRedundantRequiredOnValueType(context, operationType.Name, property);
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

    /// <summary>
    /// Detects conflicting validation range constraints on a single property, such as
    /// MinLength exceeding MaxLength or a Range where the minimum exceeds the maximum.
    /// </summary>
    private static void ValidateConflictingRanges(
        DescriptorContributorContext context,
        string operationTypeName,
        IPropertySymbol property)
    {
        var attributes = property.GetAttributes();

        // MinLength vs MaxLength conflict
        int? minLength = null;
        int? maxLength = null;
        Location? minLengthLocation = null;

        foreach (var attr in attributes)
        {
            if (ValidationSymbolHelpers.IsMinLengthAttribute(attr.AttributeClass)
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is int min)
            {
                minLength = min;
                minLengthLocation = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? property.Locations.FirstOrDefault();
            }

            if (ValidationSymbolHelpers.IsMaxLengthAttribute(attr.AttributeClass)
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is int max)
            {
                maxLength = max;
            }
        }

        if (minLength.HasValue && maxLength.HasValue && minLength.Value > maxLength.Value)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    ValidationDiagnosticDescriptors.ConflictingLengthRange,
                    minLengthLocation ?? property.Locations.FirstOrDefault(),
                    [property.Name, operationTypeName, minLength.Value.ToString(), maxLength.Value.ToString()]));
        }

        // Range(min, max) conflict
        foreach (var attr in attributes)
        {
            if (!ValidationSymbolHelpers.IsRangeAttribute(attr.AttributeClass)
                || attr.ConstructorArguments.Length != 2)
            {
                continue;
            }

            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? property.Locations.FirstOrDefault();

            if (attr.ConstructorArguments[0].Value is int intMin
                && attr.ConstructorArguments[1].Value is int intMax
                && intMin > intMax)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        ValidationDiagnosticDescriptors.ConflictingRange,
                        location,
                        [property.Name, operationTypeName, intMin.ToString(), intMax.ToString()]));
            }
            else if (attr.ConstructorArguments[0].Value is double dblMin
                     && attr.ConstructorArguments[1].Value is double dblMax
                     && dblMin > dblMax)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        ValidationDiagnosticDescriptors.ConflictingRange,
                        location,
                        [property.Name, operationTypeName, dblMin.ToString(), dblMax.ToString()]));
            }
        }

        // StringLength(max, MinimumLength=min) conflict
        foreach (var attr in attributes)
        {
            if (!ValidationSymbolHelpers.IsStringLengthAttribute(attr.AttributeClass)
                || attr.ConstructorArguments.Length != 1
                || attr.ConstructorArguments[0].Value is not int strMax)
            {
                continue;
            }

            var minArg = attr.NamedArguments.FirstOrDefault(static na => na.Key == "MinimumLength");
            if (minArg.Key is null || minArg.Value.Value is not int strMin || strMin <= strMax)
            {
                continue;
            }

            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? property.Locations.FirstOrDefault();
            context.AddDiagnostic(
                new DiagnosticInfo(
                    ValidationDiagnosticDescriptors.ConflictingStringLength,
                    location,
                    [property.Name, operationTypeName, strMax.ToString(), strMin.ToString()]));
        }
    }

    /// <summary>
    /// Detects <c>[RegularExpression("")]</c> where the pattern is empty or whitespace-only,
    /// which always matches and is almost certainly unintentional.
    /// </summary>
    private static void ValidateEmptyRegexPattern(
        DescriptorContributorContext context,
        string operationTypeName,
        IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            if (!ValidationSymbolHelpers.IsRegularExpressionAttribute(attr.AttributeClass)
                || attr.ConstructorArguments.Length < 1
                || attr.ConstructorArguments[0].Value is not string pattern)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? property.Locations.FirstOrDefault();
            context.AddDiagnostic(
                new DiagnosticInfo(
                    ValidationDiagnosticDescriptors.EmptyRegularExpressionPattern,
                    location,
                    [property.Name, operationTypeName]));
        }
    }

    /// <summary>
    /// Emits BPV012 when <c>[Required]</c> is applied to a non-nullable value type where it has no practical effect.
    /// </summary>
    private static void ValidateRedundantRequiredOnValueType(
        DescriptorContributorContext context,
        string operationTypeName,
        IPropertySymbol property)
    {
        // Only applies to non-nullable value types (int, bool, Guid, enum, etc.)
        if (property.Type.IsValueType
            && property.Type.NullableAnnotation != NullableAnnotation.Annotated
            && property.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
        {
            foreach (var attr in property.GetAttributes())
            {
                if (!ValidationSymbolHelpers.IsRequiredAttribute(attr.AttributeClass))
                {
                    continue;
                }

                var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? property.Locations.FirstOrDefault();
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        ValidationDiagnosticDescriptors.RedundantRequiredOnValueType,
                        location,
                        [property.Name, operationTypeName, property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            }
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
                case ChildObjectValidationPlan childObjectProperty:
                    RenderChildObjectValidation(context, childObjectProperty);
                    break;
                case ChildListValidationPlan childListProperty:
                    RenderChildListValidation(context, childListProperty);
                    break;
            }
        }

        context.If(
            "validationResults is not null",
            static branch => ReturnValidationFailure(
                branch,
                "new Blueprint.Runtime.Exec.ValidationFailedResult(validationResults)"));
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
                ? $"{WrapConditionOperand(successCondition)} && {failureOperand}"
                : rule.GuardCondition is string guardCondition
                    ? $"{WrapConditionOperand(guardCondition)} && {failureOperand}"
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

    private static void RenderChildObjectValidation(PipelineRenderContext context, ChildObjectValidationPlan property)
    {
        context.WriteStatement(
            $"validationResults = global::Blueprint.Validation.RecursiveDataAnnotationValidator.AppendChildObjectErrors<{property.ChildTypeName}>(\"{property.MemberName}\", {property.PropertyAccess}, new global::System.ComponentModel.DataAnnotations.ValidationContext({context.GetOperationExpression()}), validationResults);");
    }

    private static void RenderChildListValidation(PipelineRenderContext context, ChildListValidationPlan property)
    {
        context.WriteStatement(
            $"validationResults = global::Blueprint.Validation.RecursiveDataAnnotationValidator.AppendChildListErrors<{property.ElementTypeName}>(\"{property.MemberName}\", {property.PropertyAccess}, new global::System.ComponentModel.DataAnnotations.ValidationContext({context.GetOperationExpression()}), validationResults);");
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
            static branch => ReturnValidationFailure(
                branch,
                "new Blueprint.Runtime.Exec.ValidationFailedResult(validationResults)"));
        context.WriteBlankLine();
    }

    private static void ReturnValidationFailure(PipelineRenderContext context, string operationResultExpression)
    {
        context.WriteStatement($"{context.GetContextPropertyExpression("Activity")}?.SetTag(\"otel.status_code\", \"OK\");");
        context.Return(
            context.IsPipelineAsync()
                ? operationResultExpression
                : $"System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>({operationResultExpression})");
    }
}
