using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal static class ValidationDiagnosticDescriptors
{
    private const string Category = "OperationValidation";

    public static readonly DiagnosticDescriptor MissingCompareProperty = new(
        id: "BPV001",
        title: "CompareAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by CompareAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Validation attributes that name other properties must reference a real operation property so generated validation and runtime behavior stay deterministic.");

    public static readonly DiagnosticDescriptor MissingGreaterThanProperty = new(
        id: "BPV002",
        title: "GreaterThanPropertyAttribute references a missing property",
        messageFormat: "The property '{0}' referenced by GreaterThanPropertyAttribute on '{1}' does not exist on operation '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "GreaterThanPropertyAttribute must reference a real operation property so generated validation and runtime behavior stay deterministic.");
}
