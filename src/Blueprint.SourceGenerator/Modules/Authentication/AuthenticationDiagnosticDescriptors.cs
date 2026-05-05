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

    /// <summary>
    /// BPAU002: Both <c>[AllowAnonymous]</c> and <c>[MustBeAuthenticated]</c> applied to the same operation.
    /// The access mode must be unambiguous: pick one.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingAccessModeAttributes = new(
        id: "BPAU002",
        title: "Conflicting access mode attributes",
        messageFormat: "Operation '{0}' has both [AllowAnonymous] and [MustBeAuthenticated]. Remove one to make the access mode unambiguous.",
        category: "Blueprint.Authentication",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
