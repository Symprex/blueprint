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
}
