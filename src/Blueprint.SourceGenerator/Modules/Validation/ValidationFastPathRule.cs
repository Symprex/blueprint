namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Captures one generated validation rule, including the condition that fails and the attribute expression used to create
/// a matching runtime-style validation result.
/// </summary>
/// <param name="FailureCondition">The generated-code condition that indicates validation failure.</param>
/// <param name="ValidationAttributeExpression">The generated expression that recreates the validation attribute for the failure result.</param>
/// <param name="SuccessCondition">An optional success condition that later rules can reuse to avoid duplicate checks.</param>
/// <param name="GuardCondition">An optional guard that must hold before the failure condition is evaluated.</param>
/// <param name="WrapValidationAttributeExpression">Whether the attribute expression needs parentheses when embedded.</param>
internal sealed record ValidationFastPathRule(
    string FailureCondition,
    string ValidationAttributeExpression,
    string? SuccessCondition = null,
    string? GuardCondition = null,
    bool WrapValidationAttributeExpression = false);
