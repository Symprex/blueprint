namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Captures one generated validation rule, including the condition that fails and the attribute expression used to create
/// a matching runtime-style validation result.
/// </summary>
/// <param name="FailureCondition">
/// The generated-code condition that indicates validation failure. When <see cref="CachedAttributeTypeName"/> is set,
/// this condition must contain <see cref="ValidationAttributeExpression"/> literally so the module can replace it with
/// the cached static field reference.
/// </param>
/// <param name="ValidationAttributeExpression">The generated expression that recreates the validation attribute for the failure result.</param>
/// <param name="SuccessCondition">An optional success condition that later rules can reuse to avoid duplicate checks.</param>
/// <param name="GuardCondition">An optional guard that must hold before the failure condition is evaluated.</param>
/// <param name="WrapValidationAttributeExpression">Whether the attribute expression needs parentheses when embedded.</param>
/// <param name="CachedAttributeTypeName">
/// When non-null, the fully-qualified type name of the validation attribute. The module will hoist the
/// <see cref="ValidationAttributeExpression"/> to a <c>private static readonly</c> field and replace references
/// in both <see cref="FailureCondition"/> and the generated <c>FormatErrorMessage</c> call. This eliminates
/// per-request allocation for attributes whose <c>IsValid</c> call appears on the success path.
/// </param>
/// <param name="CachedRegexExpression">
/// When non-null, a <c>new Regex(pattern, RegexOptions.Compiled)</c> constructor expression that will be
/// hoisted to a <c>private static readonly Regex</c> field. The corresponding placeholder in
/// <see cref="FailureCondition"/> is replaced with the cached field reference, avoiding per-request regex
/// compilation overhead from the <c>Regex.IsMatch(string, string)</c> static method.
/// </param>
internal sealed record ValidationFastPathRule(
    string FailureCondition,
    string ValidationAttributeExpression,
    string? SuccessCondition = null,
    string? GuardCondition = null,
    bool WrapValidationAttributeExpression = false,
    string? CachedAttributeTypeName = null,
    string? CachedRegexExpression = null);
