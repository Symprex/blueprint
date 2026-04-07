using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authentication;

/// <summary>
/// Diagnostic descriptors for the authentication pipeline module.
/// </summary>
internal static class AuthenticationDiagnosticDescriptors
{
    /// <summary>
    /// BPAU001: <c>[MustBeAuthenticated]</c> on an operation that is not exposed via an HTTP host.
    /// The generated pipeline cannot enforce identity checks without an HTTP context.
    /// </summary>
    public static readonly DiagnosticDescriptor AuthAttributeOnNonHttpOperation = new(
        id: "BPAU001",
        title: "[MustBeAuthenticated] on non-HTTP operation",
        messageFormat: "Operation '{0}' is marked [MustBeAuthenticated] but is not exposed via an HTTP host. The authentication check will not be generated.",
        category: "Blueprint.Authentication",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
