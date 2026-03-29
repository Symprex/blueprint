using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Defines operation-discovery diagnostics for host-exposure validation.
/// </summary>
internal static class OperationDiscoveryDiagnosticDescriptors
{
    /// <summary>
    /// Gets the diagnostic reported when descriptor planning records host exposure that no enabled host contributor supports.
    /// </summary>
    public static DiagnosticDescriptor UnsupportedHostExposure { get; } = new(
        id: "BPOD001",
        title: "Operation host exposure is not supported by an enabled host contributor",
        messageFormat: "Operation '{1}' is exposed to host '{0}', but no enabled host contributor supports that host.",
        category: "OperationDiscovery",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Gets the diagnostic reported when descriptor planning records exposure to more than one enabled host.
    /// </summary>
    public static DiagnosticDescriptor ConflictingHostExposure { get; } = new(
        id: "BPOD002",
        title: "Operation host exposure targets multiple enabled hosts",
        messageFormat: "Operation '{0}' is exposed to multiple enabled hosts ({1}). Each operation must target exactly one host.",
        category: "OperationDiscovery",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
