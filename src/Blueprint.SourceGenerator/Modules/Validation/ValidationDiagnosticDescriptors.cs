using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Holds diagnostics emitted when validation attributes reference operation properties that do not exist.
/// </summary>
internal static class ValidationDiagnosticDescriptors
{
    private const string Category = "OperationValidation";

    /// <summary>
    /// Gets the diagnostic emitted when a <c>CompareAttribute</c> references a missing property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingCompareProperty = new(
        id: "BPV001",
        title: "CompareAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by CompareAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Validation attributes that name other properties must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>GreaterThanPropertyAttribute</c> references a missing property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingGreaterThanProperty = new(
        id: "BPV002",
        title: "GreaterThanPropertyAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by GreaterThanPropertyAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "GreaterThanPropertyAttribute must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>RequiredIfAttribute</c> references a missing property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingRequiredIfProperty = new(
        id: "BPV003",
        title: "RequiredIfAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by RequiredIfAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RequiredIfAttribute must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>RequiredIfNotAttribute</c> references a missing property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingRequiredIfNotProperty = new(
        id: "BPV004",
        title: "RequiredIfNotAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by RequiredIfNotAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RequiredIfNotAttribute must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>NotAllowedIfAttribute</c> references a missing property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingNotAllowedIfProperty = new(
        id: "BPV005",
        title: "NotAllowedIfAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by NotAllowedIfAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "NotAllowedIfAttribute must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>LessThanPropertyAttribute</c> references a missing property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingLessThanProperty = new(
        id: "BPV006",
        title: "LessThanPropertyAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by LessThanPropertyAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "LessThanPropertyAttribute must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>MustBeGreaterThanOrEqualAttribute</c> references a missing property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingMustBeGreaterThanOrEqualProperty = new(
        id: "BPV007",
        title: "MustBeGreaterThanOrEqualAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by MustBeGreaterThanOrEqualAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "MustBeGreaterThanOrEqualAttribute must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    /// <summary>
    /// Gets the diagnostic emitted when MinLength exceeds MaxLength on the same property,
    /// making the constraint unsatisfiable.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingLengthRange = new(
        id: "BPV008",
        title: "MinLength exceeds MaxLength",
        messageFormat: "Property '{0}' on operation '{1}' has MinLength({2}) > MaxLength({3}), making the constraint unsatisfiable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When both MinLengthAttribute and MaxLengthAttribute are applied to a property, the minimum must not exceed the maximum.");

    /// <summary>
    /// Gets the diagnostic emitted when a RangeAttribute has minimum greater than maximum.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingRange = new(
        id: "BPV009",
        title: "Range minimum exceeds maximum",
        messageFormat: "Property '{0}' on operation '{1}' has Range({2}, {3}) where the minimum exceeds the maximum, making the constraint unsatisfiable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a RangeAttribute is applied, the minimum must not exceed the maximum value.");

    /// <summary>
    /// Gets the diagnostic emitted when a StringLengthAttribute has MinimumLength greater than maximumLength.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingStringLength = new(
        id: "BPV010",
        title: "StringLength minimum exceeds maximum",
        messageFormat: "Property '{0}' on operation '{1}' has StringLength({2}) with MinimumLength={3}, where the minimum exceeds the maximum",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a StringLengthAttribute is applied, the MinimumLength must not exceed the maximum length.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>RegularExpressionAttribute</c> has an empty or whitespace-only pattern.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyRegularExpressionPattern = new(
        id: "BPV011",
        title: "RegularExpression has empty pattern",
        messageFormat: "Property '{0}' on operation '{1}' has a RegularExpressionAttribute with an empty or whitespace-only pattern",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A RegularExpressionAttribute with an empty pattern will match everything and is almost certainly a mistake.");

    /// <summary>
    /// Gets the diagnostic emitted when a <c>[Required]</c> attribute is applied to a non-nullable value type
    /// where the attribute has no practical effect because the property can never be <see langword="null"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor RedundantRequiredOnValueType = new(
        id: "BPV012",
        title: "[Required] is redundant on non-nullable value type",
        messageFormat: "Property '{0}' on operation '{1}' is a non-nullable value type ({2}) where [Required] has no effect — consider removing it or using '{2}?' if the property should be optional",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Applying [Required] to a non-nullable value type (int, bool, Guid, etc.) has no effect because the property can never be null. Use a nullable type (e.g. int?) if the property should be optional, or remove the attribute to reduce noise.");

    /// <summary>
    /// Gets the diagnostic emitted when an operation implements <c>IValidatableObject</c>,
    /// which prevents compile-time validation generation and forces runtime fallback.
    /// </summary>
    public static readonly DiagnosticDescriptor ValidatableObjectFallback = new(
        id: "BPV013",
        title: "IValidatableObject prevents compile-time validation",
        messageFormat: "Operation '{0}' implements IValidatableObject, which forces runtime validation fallback instead of compile-time generated checks — consider using property-level validation attributes for better performance",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Operations that implement IValidatableObject bypass the source-generated validation fast path and fall back to runtime DataAnnotation validation. Property-level attributes can be fully source-generated for better startup and request performance.");

    /// <summary>
    /// Gets the diagnostic emitted when <c>[Required]</c> and <c>[DefaultValue]</c> are both
    /// applied to the same property, creating a contradictory contract.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingRequiredAndDefault = new(
        id: "BPV014",
        title: "[Required] conflicts with [DefaultValue]",
        messageFormat: "Property '{0}' on operation '{1}' has both [Required] and [DefaultValue({2})], which is contradictory — the property can never fail the Required check because the model binder will use the default value",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Applying both [Required] and [DefaultValue] to the same property is contradictory: the model binder fills in the default value before validation runs, so the Required check can never fail. Remove one of the attributes to clarify intent.");
}
