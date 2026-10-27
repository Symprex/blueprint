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
    /// public property on the operation type, and no valid fallback path exists. This suppresses
    /// authorization code generation entirely — the operation will have NO runtime authorization
    /// enforcement. This is a security issue: fix the template or add the property.
    /// </summary>
    public static readonly DiagnosticDescriptor ResourceKeyTemplatePropertyNotFound = new(
        id: "BPAU007",
        title: "Resource key template token does not match a property",
        messageFormat: "Resource key template for operation '{0}' contains token '{{{1}}}' but no public property '{1}' exists on the operation type. Authorization enforcement is suppressed. Fix the template or add the property.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU008: <c>[Resource]</c> or <c>[FromResource]</c> declared on an operation that is not exposed
    /// via an HTTP host. The pipeline authorization check will not be generated because authorization
    /// requires an HTTP context to extract the authenticated principal. The descriptor metadata is still
    /// projected, but the runtime enforcement is absent — which is almost certainly a misconfiguration.
    /// </summary>
    public static readonly DiagnosticDescriptor ResourceOnNonHttpOperation = new(
        id: "BPAU008",
        title: "[Resource] on non-HTTP operation",
        messageFormat: "Operation '{0}' declares resource authorization requirements but is not exposed via an HTTP host. The authorization check will not be generated. Add an HTTP link or remove the authorization attributes.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU009: The same noun+verb pair appears more than once in <c>[Resource]</c> attributes on a single operation.
    /// Duplicate scope pairs produce redundant authorization metadata and almost always indicate a copy-paste error.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateResourceScopePair = new(
        id: "BPAU009",
        title: "Duplicate [Resource] scope pair",
        messageFormat: "Operation '{0}' declares [Resource(\"{1}\", \"{2}\")] more than once. Remove the duplicate attribute.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU010: A fallback property path in a resource key template (the part after <c>:</c> in
    /// <c>{Primary:Fallback}</c>) is never used: the operation has the primary but not the fallback, and no resource type
    /// the operation links from both lacks the primary and has the fallback. The fallback is dead code. Remove it to
    /// silence this warning.
    /// </summary>
    public static readonly DiagnosticDescriptor ResourceKeyTemplateFallbackPropertyNotFound = new(
        id: "BPAU010",
        title: "Resource key template fallback property does not exist",
        messageFormat: "Resource key template for operation '{0}' has fallback '{{{1}}}' but no public property '{1}' exists on the operation type, or on a resource type it links from that lacks the primary. The primary property is used; remove the fallback to silence this warning.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// BPAU011: A token in a resource key template matches no public property of a resource type the operation links
    /// from (<c>[Link]</c> or <c>[SelfLink]</c>): neither the primary nor, for <c>{Primary:Fallback}</c>, the fallback.
    /// The key cannot be resolved against that resource, so a link from it could never be authorised.
    /// </summary>
    public static readonly DiagnosticDescriptor ResourceKeyTemplateTokenNotFoundOnLinkedResource = new(
        id: "BPAU011",
        title: "Resource key template token does not match a property of a linked resource type",
        messageFormat: "Resource key template for operation '{0}' contains token '{{{1}}}', which matches no public property of '{2}', a resource type the operation links from. A link from '{2}' cannot be authorised. Fix the template or add the property to '{2}'.",
        category: "Blueprint.Authorization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
