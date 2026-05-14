using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostic descriptors for projecting HTTP method metadata into operation descriptors.
/// </summary>
internal static class OperationHttpMethodMetadataDiagnosticDescriptors
{
    /// <summary>
    /// BPOA001: Explicit HttpMethod attributes must provide a non-empty HTTP method token.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidExplicitHttpMethod = new(
        id: "BPOA001",
        title: "Explicit HTTP method is invalid",
        messageFormat: "The operation '{0}' declares an explicit HTTP method that is null, empty, or whitespace-only",
        category: "OpenApi",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPOA002: Multiple distinct HTTP method attributes on a single operation usually indicates a mistake.
    /// Each Blueprint operation should map to exactly one HTTP method; use separate operations for
    /// different methods on the same resource.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingHttpMethods = new(
        id: "BPOA002",
        title: "Operation declares multiple conflicting HTTP methods",
        messageFormat: "The operation '{0}' declares multiple HTTP method attributes ({1}). Each operation should use exactly one HTTP method.",
        category: "OpenApi",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
