namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Captures the validation strategy chosen for an operation, including whether generation must fall back to whole-object
/// runtime validation.
/// </summary>
/// <param name="RequiresWholeObjectFallback">Whether property-level generation is unsafe and runtime validation must own the whole object.</param>
/// <param name="Properties">The per-property validation plans when fast-path generation is possible.</param>
internal sealed record ValidationPipelinePlan(
    bool RequiresWholeObjectFallback,
    IReadOnlyList<PropertyValidationPlan> Properties)
{
    /// <summary>
    /// Gets a value indicating whether the operation needs any generated or runtime validation at all.
    /// </summary>
    public bool RequiresValidation => this.RequiresWholeObjectFallback || this.Properties.Count > 0;
}

/// <summary>
/// Base record for the validation strategy selected for a single property.
/// </summary>
/// <param name="MemberName">The property name used in diagnostics and runtime fallback calls.</param>
/// <param name="PropertyAccess">The generated-code expression used to read the property value.</param>
internal abstract record PropertyValidationPlan(
    string MemberName,
    string PropertyAccess);

/// <summary>
/// Indicates that a property must use the existing runtime validation path because its attribute shape is not safe to
/// reduce to generated conditions.
/// </summary>
/// <param name="MemberName">The property name used in diagnostics and runtime fallback calls.</param>
/// <param name="PropertyAccess">The generated-code expression used to read the property value.</param>
internal sealed record RuntimeFallbackPropertyValidationPlan(
    string MemberName,
    string PropertyAccess)
    : PropertyValidationPlan(MemberName, PropertyAccess);

/// <summary>
/// Indicates that a property can be validated through generated conditions and generated validation-attribute instances.
/// </summary>
/// <param name="MemberName">The property name used in diagnostics and runtime fallback calls.</param>
/// <param name="DisplayName">The display name currently used when creating validation results.</param>
/// <param name="PropertyAccess">The generated-code expression used to read the property value.</param>
/// <param name="RequiredRule">The required-like rule that must run before other rules, if one exists.</param>
/// <param name="OtherRules">Additional non-required rules that can run after the required guard passes.</param>
internal sealed record FastPathPropertyValidationPlan(
    string MemberName,
    string DisplayName,
    string PropertyAccess,
    ValidationFastPathRule? RequiredRule,
    IReadOnlyList<ValidationFastPathRule> OtherRules)
    : PropertyValidationPlan(MemberName, PropertyAccess);

/// <summary>
/// Indicates that a property is a complex child object whose type has its own validation attributes and should be
/// recursively validated using <c>RecursiveDataAnnotationValidator.ValidateChildObject</c>.
/// </summary>
/// <param name="MemberName">The property name used in path prefixes.</param>
/// <param name="PropertyAccess">The generated-code expression used to read the property value.</param>
/// <param name="ChildTypeName">The fully-qualified type name of the child type for the generic call.</param>
internal sealed record ChildObjectValidationPlan(
    string MemberName,
    string PropertyAccess,
    string ChildTypeName)
    : PropertyValidationPlan(MemberName, PropertyAccess);

/// <summary>
/// Indicates that a property is a list of complex child objects that should be recursively validated using
/// <c>RecursiveDataAnnotationValidator.ValidateChildList</c>.
/// </summary>
/// <param name="MemberName">The property name used in path prefixes.</param>
/// <param name="PropertyAccess">The generated-code expression used to read the property value.</param>
/// <param name="ElementTypeName">The fully-qualified type name of the list element for the generic call.</param>
internal sealed record ChildListValidationPlan(
    string MemberName,
    string PropertyAccess,
    string ElementTypeName)
    : PropertyValidationPlan(MemberName, PropertyAccess);
