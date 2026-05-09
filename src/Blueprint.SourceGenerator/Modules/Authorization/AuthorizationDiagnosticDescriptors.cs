using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Diagnostic descriptors for the authorization pipeline module.
/// </summary>
internal static class AuthorizationDiagnosticDescriptors
{
    /// <summary>
    /// BPAU003: <c>[Resource]</c> or <c>[FromResource]</c> combined with <c>[AllowAnonymous]</c>.
    /// Operations that declare resource permission requirements cannot simultaneously allow anonymous access.
    /// </summary>
    public static readonly DiagnosticDescriptor ResourceWithAllowAnonymous = new(
        id: "BPAU003",
        title: "Authorization requirement combined with [AllowAnonymous]",
        messageFormat: "Operation '{0}' declares resource authorization requirements but is also marked [AllowAnonymous]. Remove [AllowAnonymous] or remove the authorization attributes.",
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

    /// <summary>
    /// BPAU005: Both <c>[FromResource]</c> and <c>[Resource]</c> applied to the same operation.
    /// Use one mechanism or the other to declare scope pairs.
    /// </summary>
    public static readonly DiagnosticDescriptor FromResourceWithDirectResource = new(
        id: "BPAU005",
        title: "[FromResource] combined with [Resource]",
        messageFormat: "Operation '{0}' has both [FromResource] and [Resource]. Use one mechanism to declare scope pairs, not both.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU006: <c>[FromResource(typeof(X))]</c> referencing a type that has no <c>[Resource]</c> attributes.
    /// The referenced type must declare at least one scope pair to inherit.
    /// </summary>
    public static readonly DiagnosticDescriptor FromResourceTargetHasNoResource = new(
        id: "BPAU006",
        title: "[FromResource] target has no [Resource] attributes",
        messageFormat: "Operation '{0}' uses [FromResource(typeof({1}))] but '{1}' does not declare any [Resource] attributes. Add [Resource] to the target or remove [FromResource].",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU007: A <c>{PropertyName}</c> token in a resource key template does not match any
    /// public property on the operation type (or the <c>[FromResource]</c> source operation).
    /// </summary>
    public static readonly DiagnosticDescriptor ResourceKeyTemplatePropertyNotFound = new(
        id: "BPAU007",
        title: "Resource key template token does not match a property",
        messageFormat: "Resource key template for operation '{0}' contains token '{{{1}}}' but no public property '{1}' exists on the operation type. Fix the template or add the property.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
