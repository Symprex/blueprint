using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

internal static class FoundationModuleDiagnosticDescriptors
{
    private const string Category = "BlueprintModuleSystem";

    public static readonly DiagnosticDescriptor DuplicateUniqueContributor = new(
        id: "BPMOD001",
        title: "Duplicate unique contributor",
        messageFormat: "The unique contributor '{0}' for capability '{1}' is configured more than once: {2}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Contributor composition must be deterministic. Unique contributors may only appear once for a given capability.");

    public static readonly DiagnosticDescriptor MissingContributorDependency = new(
        id: "BPMOD002",
        title: "Missing contributor dependency",
        messageFormat: "The contributor '{0}' for capability '{1}' depends on '{2}', but no matching contributor is configured",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Contributor dependency declarations must resolve to configured contributors.");

    public static readonly DiagnosticDescriptor CyclicContributorDependency = new(
        id: "BPMOD003",
        title: "Cyclic contributor dependency",
        messageFormat: "The configured contributors for capability '{0}' contain a dependency cycle: {1}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Contributor dependencies must form a deterministic acyclic ordering.");
}
