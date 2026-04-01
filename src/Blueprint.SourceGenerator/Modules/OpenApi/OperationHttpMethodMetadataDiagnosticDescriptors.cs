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
}
