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
}
