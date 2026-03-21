namespace Blueprint.SourceGenerator.Modules;

internal sealed record ValidationFastPathRule(
    string FailureCondition,
    string ValidationAttributeExpression,
    string? SuccessCondition = null,
    string? GuardCondition = null,
    bool WrapValidationAttributeExpression = false);
