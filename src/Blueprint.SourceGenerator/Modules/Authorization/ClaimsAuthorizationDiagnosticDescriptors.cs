using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Diagnostic descriptors for the compile-time claims authorization module.
/// </summary>
internal static class ClaimsAuthorizationDiagnosticDescriptors
{
    /// <summary>
    /// BPAU008: <c>[Permission]</c> or <c>[PermissionFor]</c> combined with <c>[AllowAnonymous]</c>.
    /// Claims-based permissions require authentication and cannot coexist with anonymous access.
    /// </summary>
    public static readonly DiagnosticDescriptor ClaimsWithAllowAnonymous = new(
        id: "BPAU008",
        title: "Claims authorization combined with [AllowAnonymous]",
        messageFormat: "Operation '{0}' declares claims-based permissions ([Permission] or [PermissionFor]) but is also marked [AllowAnonymous]. Remove [AllowAnonymous] or remove the claims attributes.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU009: A <c>{PropertyName}</c> token in a <c>[PermissionFor]</c> resource key template
    /// does not match any public property on the operation type.
    /// </summary>
    public static readonly DiagnosticDescriptor ClaimsTemplatePropertyNotFound = new(
        id: "BPAU009",
        title: "Claims template token does not match a property",
        messageFormat: "Claims template for operation '{0}' contains token '{{{1}}}' but no public property '{1}' exists on the operation type. Fix the template or add the property.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
