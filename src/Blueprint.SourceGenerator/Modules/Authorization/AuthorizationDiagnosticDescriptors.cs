using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Diagnostic descriptors for the authorization pipeline module.
/// </summary>
internal static class AuthorizationDiagnosticDescriptors
{
    /// <summary>
    /// BPAU003: <c>[Resource]</c> combined with <c>[AllowAnonymous]</c>. Operations that declare
    /// resource permission requirements cannot simultaneously allow anonymous access.
    /// </summary>
    public static readonly DiagnosticDescriptor ResourceWithAllowAnonymous = new(
        id: "BPAU003",
        title: "[Resource] combined with [AllowAnonymous]",
        messageFormat: "Operation '{0}' declares [Resource] permission requirements but is also marked [AllowAnonymous]. Remove [AllowAnonymous] or remove the [Resource] attributes.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU004: <c>[Resource]</c> with an empty or whitespace-only noun or verb value.
    /// Scope pairs must be non-empty to produce meaningful authorization metadata.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyResourceScopePairValue = new(
        id: "BPAU004",
        title: "Empty scope pair value in [Resource]",
        messageFormat: "Operation '{0}' has a [Resource] attribute with an empty or whitespace-only {1}. Provide a meaningful value.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
