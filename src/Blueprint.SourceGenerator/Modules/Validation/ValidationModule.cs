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
        ValidateIValidatableObjectFallback(context);
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

        // Hoist validation attributes and compiled regex patterns to static readonly fields.
        // This eliminates per-request allocation for attributes and avoids per-request pattern compilation.
        var cachedFields = EmitCachedAttributeFields(context, validationPlan.Properties);

        context.AddRenderStep(PipelineStage.Validation, renderContext =>
        {
            if (validationPlan.Properties.Count > 0)
            {
                RenderPropertyValidation(renderContext, validationPlan.Properties, cachedFields);
            }

            if (validationPlan.ImplementsIValidatableObject)
            {
                RenderIValidatableObjectTailCall(renderContext);
            }
        });
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
            ValidateConflictingRequiredAndDefault(context, operationType.Name, property);
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

    /// <summary>
    /// Emits BPV013 when the operation implements <c>IValidatableObject</c>, which forces
    /// runtime validation fallback instead of compile-time generated checks.
    /// </summary>
    private static void ValidateIValidatableObjectFallback(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        if (!ValidationSymbolHelpers.ImplementsIValidatableObject(operationType))
        {
            return;
        }

        context.AddDiagnostic(
            new DiagnosticInfo(
                ValidationDiagnosticDescriptors.ValidatableObjectFallback,
                operationType.Locations.FirstOrDefault(),
                [operationType.Name]));
    }

    /// <summary>
    /// Emits BPV014 when <c>[Required]</c> and <c>[DefaultValue]</c> are both applied to the
    /// same property, creating a contradictory contract where Required can never fail.
    /// </summary>
    private static void ValidateConflictingRequiredAndDefault(
        DescriptorContributorContext context,
        string operationTypeName,
        IPropertySymbol property)
    {
        var attributes = property.GetAttributes();
        AttributeData? requiredAttr = null;
        object? defaultValue = null;

        foreach (var attr in attributes)
        {
            if (ValidationSymbolHelpers.IsRequiredAttribute(attr.AttributeClass))
            {
                requiredAttr = attr;
            }

            if (attr.AttributeClass is { MetadataName: "DefaultValueAttribute" } defaultValueAttrClass
                && defaultValueAttrClass.IsInNamespace("System", "ComponentModel")
                && attr.ConstructorArguments.Length >= 1)
            {
                defaultValue = attr.ConstructorArguments[0].Value;
            }
        }

        if (requiredAttr is null || defaultValue is null)
        {
            return;
        }

        var location = requiredAttr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                       ?? property.Locations.FirstOrDefault();

        context.AddDiagnostic(
            new DiagnosticInfo(
                ValidationDiagnosticDescriptors.ConflictingRequiredAndDefault,
                location,
                [property.Name, operationTypeName, defaultValue.ToString() ?? "null"]));
    }

    /// <summary>
    /// Scans validation rules for attributes and regex patterns that should be cached as static readonly fields.
    /// Attributes with <see cref="ValidationFastPathRule.CachedAttributeTypeName"/> set have their error-message
    /// call on the success path, so hoisting them avoids per-request allocation. Rules with
    /// <see cref="ValidationFastPathRule.CachedRegexExpression"/> set emit a compiled <c>Regex</c> instance
    /// field, eliminating per-request pattern compilation from the static <c>Regex.IsMatch(string, string)</c>.
    /// </summary>
    private static CachedFieldMaps EmitCachedAttributeFields(
        PipelineContributorContext context,
        IReadOnlyList<PropertyValidationPlan> properties)
    {
        var attributeFieldMap = new Dictionary<string, string>();
        var regexFieldMap = new Dictionary<string, string>();

        foreach (var property in properties)
        {
            if (property is not FastPathPropertyValidationPlan fastPath)
            {
                continue;
            }

            CacheRuleIfNeeded(context, fastPath.MemberName, fastPath.RequiredRule, attributeFieldMap, regexFieldMap);

            foreach (var rule in fastPath.OtherRules)
            {
                CacheRuleIfNeeded(context, fastPath.MemberName, rule, attributeFieldMap, regexFieldMap);
            }
        }

        return new CachedFieldMaps(attributeFieldMap, regexFieldMap);

        static void CacheRuleIfNeeded(
            PipelineContributorContext context,
            string memberName,
            ValidationFastPathRule? rule,
            Dictionary<string, string> attributeFieldMap,
            Dictionary<string, string> regexFieldMap)
        {
            if (rule is null)
            {
                return;
            }

            // Cache the validation attribute as a static readonly field when it has a type name.
            if (rule.CachedAttributeTypeName is not null)
            {
                var expression = rule.ValidationAttributeExpression;

                if (!attributeFieldMap.ContainsKey(expression))
                {
                    var fieldName = $"_va_{memberName}";

                    if (attributeFieldMap.ContainsValue(fieldName))
                    {
                        fieldName = $"_va_{memberName}_{attributeFieldMap.Count}";
                    }

                    attributeFieldMap[expression] = fieldName;

                    var typeName = rule.CachedAttributeTypeName;
                    context.AddDeferredClassMember(classBuilder =>
                    {
                        classBuilder.AddProperty(fieldName, Accessibility.Private)
                            .SetType(typeName)
                            .MakeStatic()
                            .WithReadonlyValue(expression);
                    });
                }
            }

            // Cache compiled Regex instances as static readonly fields to avoid per-request pattern compilation.
            if (rule.CachedRegexExpression is not null)
            {
                var regexExpression = rule.CachedRegexExpression;

                if (!regexFieldMap.ContainsKey(regexExpression))
                {
                    var fieldName = $"_vr_{memberName}";

                    if (regexFieldMap.ContainsValue(fieldName))
                    {
                        fieldName = $"_vr_{memberName}_{regexFieldMap.Count}";
                    }

                    regexFieldMap[regexExpression] = fieldName;

                    context.AddDeferredClassMember(classBuilder =>
                    {
                        classBuilder.AddProperty(fieldName, Accessibility.Private)
                            .SetType("System.Text.RegularExpressions.Regex")
                            .MakeStatic()
                            .WithReadonlyValue(regexExpression);
                    });
                }
            }
        }
    }

    /// <summary>
    /// Holds the mappings from inline constructor expressions to their cached static field names for
    /// both validation attributes and compiled regex instances.
    /// </summary>
    private readonly record struct CachedFieldMaps(
        Dictionary<string, string> AttributeFields,
        Dictionary<string, string> RegexFields);

    private static void RenderPropertyValidation(PipelineRenderContext context, IReadOnlyList<PropertyValidationPlan> properties, CachedFieldMaps cachedFields)
    {
        context.DeclareVariable(
            "validationResults",
            "default(global::System.Collections.Generic.List<global::System.ComponentModel.DataAnnotations.ValidationResult>)");

        foreach (var property in properties)
        {
            switch (property)
            {
                case FastPathPropertyValidationPlan fastPathProperty:
                    RenderFastPathValidation(context, fastPathProperty, cachedFields);
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
            static branch => ReturnValidationFailure(branch, "validationResults"));
        context.WriteBlankLine();
    }

    private static void RenderFastPathValidation(PipelineRenderContext context, FastPathPropertyValidationPlan property, CachedFieldMaps cachedFields)
    {
        if (property.RequiredRule is not null)
        {
            RenderValidationFailure(context, ResolveFailureCondition(property.RequiredRule, cachedFields), property.RequiredRule, property, cachedFields);
        }

        foreach (var rule in property.OtherRules)
        {
            var failureOperand = WrapConditionOperand(ResolveFailureCondition(rule, cachedFields));
            var failureCondition = property.RequiredRule?.SuccessCondition is string successCondition
                ? $"{WrapConditionOperand(successCondition)} && {failureOperand}"
                : rule.GuardCondition is string guardCondition
                    ? $"{WrapConditionOperand(guardCondition)} && {failureOperand}"
                    : ResolveFailureCondition(rule, cachedFields);
            RenderValidationFailure(context, failureCondition, rule, property, cachedFields);
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
        FastPathPropertyValidationPlan property,
        CachedFieldMaps cachedFields)
    {
        var attrExpression = ResolveAttributeExpression(rule, cachedFields.AttributeFields);
        var validationAttributeExpression = rule.WrapValidationAttributeExpression
            ? $"({attrExpression})"
            : attrExpression;

        context.If(
            failureCondition,
            branch =>
            {
                branch.WriteStatement(
                    $"Blueprint.Runtime.Exec.PipelineHelpers.AppendValidationError(ref validationResults, {validationAttributeExpression}.FormatErrorMessage(\"{property.DisplayName}\"), \"{property.MemberName}\");");
            });
    }

    /// <summary>
    /// Returns the attribute reference to use in generated code. When the rule has a cached static field,
    /// returns the field name; otherwise returns the inline constructor expression.
    /// </summary>
    private static string ResolveAttributeExpression(ValidationFastPathRule rule, Dictionary<string, string> attributeFields)
    {
        return attributeFields.TryGetValue(rule.ValidationAttributeExpression, out var fieldName)
            ? fieldName
            : rule.ValidationAttributeExpression;
    }

    /// <summary>
    /// Returns the failure condition with any cached attribute or regex expression replaced by its static field reference.
    /// </summary>
    private static string ResolveFailureCondition(ValidationFastPathRule rule, CachedFieldMaps cachedFields)
    {
        var condition = rule.FailureCondition;

        // Replace the regex placeholder with the cached Regex field name.
        if (rule.CachedRegexExpression is not null
            && cachedFields.RegexFields.TryGetValue(rule.CachedRegexExpression, out var regexFieldName))
        {
            condition = condition.Replace(
                RegularExpressionValidationFastPathStrategy.FieldPlaceholder,
                regexFieldName,
                StringComparison.Ordinal);
        }

        // Replace the attribute constructor expression with the cached attribute field name.
        if (rule.CachedAttributeTypeName is not null
            && cachedFields.AttributeFields.TryGetValue(rule.ValidationAttributeExpression, out var attrFieldName))
        {
            condition = condition.Replace(rule.ValidationAttributeExpression, attrFieldName, StringComparison.Ordinal);
        }

        return condition;
    }

    /// <summary>
    /// Emits a single call to <c>PipelineHelpers.ValidateIValidatableObject</c> that runs after generated
    /// property-level checks. Unlike <see cref="RenderGenericValidation"/> which delegates ALL validation to
    /// runtime, this only invokes the custom cross-property logic the type author wrote, while property-level
    /// DataAnnotations have already been checked by generated code.
    /// </summary>
    /// <remarks>
    /// The validation loop, lazy list allocation, and failure-result creation are all handled by the shared
    /// runtime helper, keeping each pipeline to a 2-line call + guard instead of ~10 lines of inline code.
    /// </remarks>
    private static void RenderIValidatableObjectTailCall(PipelineRenderContext context)
    {
        var operation = context.GetOperationExpression();

        context.DeclareVariable(
            "_ivoResult",
            $"Blueprint.Runtime.Exec.PipelineHelpers.ValidateIValidatableObject(context, {operation})");
        context.If("_ivoResult != null", branch =>
        {
            branch.Return("_ivoResult");
        });
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
            static branch => ReturnValidationFailure(branch, "validationResults"));
        context.WriteBlankLine();
    }

    private static void ReturnValidationFailure(PipelineRenderContext context, string validationResultsVariable)
    {
        var resultExpression = $"Blueprint.Runtime.Exec.PipelineHelpers.CreateValidationFailure({context.GetContextExpression()}, {validationResultsVariable})";
        context.Return(
            context.IsPipelineAsync()
                ? resultExpression
                : $"System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>({resultExpression})");
    }
}
