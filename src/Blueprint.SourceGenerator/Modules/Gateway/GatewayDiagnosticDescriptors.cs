using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// Diagnostic descriptors for gateway operations: <c>[Expose&lt;TInner&gt;]</c> and the forwarding handler the pipeline
/// generator synthesises for it (Symprex/blueprint#59, D17).
/// </summary>
internal static class GatewayDiagnosticDescriptors
{
    private const string Category = "Blueprint.Gateway";

    /// <summary>
    /// BPGW001: <c>[Expose&lt;TInner&gt;]</c> in a project that sets <c>BlueprintGatewayAllowExpose</c> to <c>false</c> (D26).
    /// </summary>
    public static readonly DiagnosticDescriptor ExposeNotAllowed = new(
        id: "BPGW001",
        title: "[Expose<TInner>] is not allowed in this project",
        messageFormat: "Operation '{0}' uses [Expose<{1}>], but this project sets BlueprintGatewayAllowExpose to false. Declare the operation's properties itself instead, so that its public contract is written out.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW002: a gateway operation that declares none of Blueprint's native authorisation attributes (D13).
    /// </summary>
    public static readonly DiagnosticDescriptor MissingAuthorisationDeclaration = new(
        id: "BPGW002",
        title: "Gateway operation must declare its authorisation",
        messageFormat: "Gateway operation '{0}' must declare its authorisation with [AllowAnonymous], [MustBeAuthenticated], [Resource] or [FromResource] from Blueprint.Model.Attributes. The inner operation's authorisation is never copied.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW003: no <c>[BlueprintClient]</c> visible to the gateway includes the inner operation (D12).
    /// </summary>
    public static readonly DiagnosticDescriptor NoClientIncludesInnerOperation = new(
        id: "BPGW003",
        title: "No [BlueprintClient] includes the inner operation",
        messageFormat: "Gateway operation '{0}' forwards to '{1}', but no [BlueprintClient] class visible to this project includes it. Add [BlueprintClientInclude(typeof({1}))] to the typed client of the upstream service.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW004: more than one <c>[BlueprintClient]</c> includes the inner operation, so the upstream is ambiguous.
    /// </summary>
    public static readonly DiagnosticDescriptor SeveralClientsIncludeInnerOperation = new(
        id: "BPGW004",
        title: "More than one [BlueprintClient] includes the inner operation",
        messageFormat: "Gateway operation '{0}' forwards to '{1}', which more than one [BlueprintClient] includes ({2}), so the upstream to call is ambiguous. Include it in one client only.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW005: a hand-written handler on an operation whose handler is generated (D12).
    /// </summary>
    public static readonly DiagnosticDescriptor HandWrittenHandlerOnForwardedOperation = new(
        id: "BPGW005",
        title: "A gateway operation's handler is generated",
        messageFormat: "Gateway operation '{0}' has a hand-written handler ({1}), but [Expose<{2}>] generates its handler. Remove the handler, or replace [Expose<{2}>] with properties and a handler of its own.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW006: an <c>[Expose&lt;TInner&gt;]</c> class that declares no operation interface (D9).
    /// </summary>
    public static readonly DiagnosticDescriptor MissingOperationInterface = new(
        id: "BPGW006",
        title: "An exposing operation must declare its operation interface",
        messageFormat: "'{0}' uses [Expose<{1}>] but declares no operation interface, so no generator treats it as an operation. Declare it, for example 'partial class {0} : {2}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW007: an operation carries more than one upstream credential attribute (D7).
    /// </summary>
    public static readonly DiagnosticDescriptor SeveralUpstreamCredentials = new(
        id: "BPGW007",
        title: "A gateway operation selects one upstream credential",
        messageFormat: "Gateway operation '{0}' carries {1} upstream credential attributes ({2}), but its forwards authenticate with one strategy. Keep one.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW008: an upstream credential attribute selects a type that is not an <c>IUpstreamCredentialStrategy</c> (D7).
    /// </summary>
    public static readonly DiagnosticDescriptor CredentialTypeIsNotAStrategy = new(
        id: "BPGW008",
        title: "The upstream credential type must be a strategy",
        messageFormat: "Gateway operation '{0}' selects '{1}' as its upstream credential strategy, but '{1}' does not implement Blueprint.HttpClient.IUpstreamCredentialStrategy",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW009: a <c>[TranslateError]</c> names no static <c>ApiExceptionFactory</c> of the class it is applied to (D8).
    /// </summary>
    public static readonly DiagnosticDescriptor TranslateErrorTargetIsNotAFactory = new(
        id: "BPGW009",
        title: "[TranslateError] must name a static ApiExceptionFactory",
        messageFormat: "[TranslateError(\"{0}\", \"{1}\")] on '{2}' names no static ApiExceptionFactory field or property of '{2}' called '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
