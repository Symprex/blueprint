namespace Blueprint.SourceGenerator.Modules;

internal sealed record ValidationPipelinePlan(
    bool RequiresWholeObjectFallback,
    IReadOnlyList<PropertyValidationPlan> Properties)
{
    public bool RequiresValidation => this.RequiresWholeObjectFallback || this.Properties.Count > 0;
}

internal abstract record PropertyValidationPlan(
    string MemberName,
    string PropertyAccess);

internal sealed record RuntimeFallbackPropertyValidationPlan(
    string MemberName,
    string PropertyAccess)
    : PropertyValidationPlan(MemberName, PropertyAccess);

internal sealed record FastPathPropertyValidationPlan(
    string MemberName,
    string DisplayName,
    string PropertyAccess,
    ValidationFastPathRule? RequiredRule,
    IReadOnlyList<ValidationFastPathRule> OtherRules)
    : PropertyValidationPlan(MemberName, PropertyAccess);
