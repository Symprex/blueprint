using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Holds diagnostics that describe invalid Blueprint contributor composition or pipeline-planning state.
/// </summary>
internal static class FoundationModuleDiagnosticDescriptors
{
    private const string Category = "BlueprintModuleSystem";

    /// <summary>
    /// Gets the diagnostic emitted when two contributors claim the same unique capability.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateUniqueContributor = new(
        id: "BPMOD001",
        title: "Duplicate unique contributor",
        messageFormat: "The unique contributor '{0}' for capability '{1}' is configured more than once: {2}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Contributor composition must be deterministic. Unique contributors may only appear once for a given capability.");

    /// <summary>
    /// Gets the diagnostic emitted when a contributor dependency cannot be resolved.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingContributorDependency = new(
        id: "BPMOD002",
        title: "Missing contributor dependency",
        messageFormat: "The contributor '{0}' for capability '{1}' depends on '{2}', but no matching contributor is configured",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Contributor dependency declarations must resolve to configured contributors.");

    /// <summary>
    /// Gets the diagnostic emitted when contributor dependencies form a cycle.
    /// </summary>
    public static readonly DiagnosticDescriptor CyclicContributorDependency = new(
        id: "BPMOD003",
        title: "Cyclic contributor dependency",
        messageFormat: "The configured contributors for capability '{0}' contain a dependency cycle: {1}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Contributor dependencies must form a deterministic acyclic ordering.");

    /// <summary>
    /// Gets the diagnostic emitted when a contributor consumes a variable before any configured source can provide it.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingRequiredVariable = new(
        id: "BPMOD004",
        title: "Missing required variable",
        messageFormat: "The contributor '{0}' requires variable '{1}' of type '{2}' by stage '{3}', but the earliest matching provision is available from '{4}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Required pipeline variables must be provided and available no later than the consuming contributor stage.");

    /// <summary>
    /// Gets the diagnostic emitted when a public planning seam is asked to build data for an operation that does not
    /// exist in the current compilation.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingOperationType = new(
        id: "BPMOD005",
        title: "Missing operation type",
        messageFormat: "The operation type '{0}' could not be found while building capability '{1}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Public planning helpers should fail deterministically when the requested operation metadata name does not resolve.");

    /// <summary>
    /// Gets the diagnostic emitted when multiple debug-manifest contributors try to own the same entry key.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateDebugManifestEntry = new(
        id: "BPMOD006",
        title: "Duplicate debug-manifest entry",
        messageFormat: "The debug-manifest contributor '{0}' attempted to write entry '{1}' for operation '{3}', but contributor '{2}' already owns that key",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Debug-manifest entries are contributor-owned so inspection artifacts stay deterministic and explainable as more modules participate.");
}
