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
    /// BPGW007: a <c>[Bind]</c> or <c>[Hide]</c> naming a property the inner operation has no public settable property of (D11).
    /// </summary>
    public static readonly DiagnosticDescriptor BoundPropertyNotOnInnerOperation = new(
        id: "BPGW007",
        title: "A binding names a property the inner operation does not have",
        messageFormat: "Gateway operation '{0}' has [{1}(\"{2}\")], but '{3}' has no public settable property '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW008: a <c>[Bind]</c> that sets no source or several, or a <c>[GatewayBind]</c> without <c>FromClaim</c> (D11).
    /// </summary>
    public static readonly DiagnosticDescriptor BindingNeedsOneSource = new(
        id: "BPGW008",
        title: "A binding must set exactly one source",
        messageFormat: "[{0}(\"{1}\")] must set exactly one of {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW009: a <c>[Hide]</c> on a property the inner operation requires (D11, edge cases).
    /// </summary>
    public static readonly DiagnosticDescriptor HiddenPropertyIsRequired = new(
        id: "BPGW009",
        title: "A required inner property cannot be hidden",
        messageFormat: "Gateway operation '{0}' hides '{1}', which '{2}' requires. Bind it with [Bind] instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW010: an inner route placeholder that the gateway's routes lack and that is neither bound nor hidden (D11, edge cases).
    /// </summary>
    public static readonly DiagnosticDescriptor UnboundInnerRoutePlaceholder = new(
        id: "BPGW010",
        title: "An inner route placeholder must be in the gateway's route, bound or hidden",
        messageFormat: "Gateway operation '{0}' does not carry '{1}' in its route, but '{2}' routes by it ('{3}'). Add it to the gateway's route, or bind it with [GatewayBind] or [Bind], or hide it with [Hide].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW011: a binding whose source does not convert to the inner property's type (D11).
    /// </summary>
    public static readonly DiagnosticDescriptor BindingDoesNotConvert = new(
        id: "BPGW011",
        title: "A binding's source does not convert to the inner property's type",
        messageFormat: "Gateway operation '{0}' binds '{1}' from {2}, which does not convert to '{3}'. A claim converts to any type a route value does; a property or value has to be of the inner property's type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW012: a <c>[Bind(FromRoute)]</c> or <c>[Bind(FromProperty)]</c> naming something the gateway operation does not have (D11).
    /// </summary>
    public static readonly DiagnosticDescriptor BindingSourceNotOnOperation = new(
        id: "BPGW012",
        title: "A binding's source is not on the gateway operation",
        messageFormat: "Gateway operation '{0}' binds '{1}' from {2}, which it does not declare. A route placeholder has to be in the gateway's own route and bind a property it declares.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW013: one inner property bound or hidden more than once on one operation (D11).
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyBoundTwice = new(
        id: "BPGW013",
        title: "A property can be bound or hidden only once per operation",
        messageFormat: "Gateway operation '{0}' binds or hides '{1}' more than once",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW014: an <c>OnMap</c> or <c>OnMapAsync</c> the generated forward cannot call (D11).
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidMapHook = new(
        id: "BPGW014",
        title: "A gateway operation's map hook has the wrong shape",
        messageFormat: "'{0}.{1}' is not a map hook the generated forward can call: it must be a single internal or public instance method whose first parameter is '{2}', and OnMap returns void while OnMapAsync returns ValueTask or Task",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW015: an operation carries more than one upstream credential attribute (D7).
    /// </summary>
    public static readonly DiagnosticDescriptor SeveralUpstreamCredentials = new(
        id: "BPGW015",
        title: "A gateway operation selects one upstream credential",
        messageFormat: "Gateway operation '{0}' carries {1} upstream credential attributes ({2}), but its forwards authenticate with one strategy. Keep one.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW016: an upstream credential attribute selects a type that is not an <c>IUpstreamCredentialStrategy</c> (D7).
    /// </summary>
    public static readonly DiagnosticDescriptor CredentialTypeIsNotAStrategy = new(
        id: "BPGW016",
        title: "The upstream credential type must be a strategy",
        messageFormat: "Gateway operation '{0}' selects '{1}' as its upstream credential strategy, but '{1}' does not implement Blueprint.HttpClient.IUpstreamCredentialStrategy",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW017: a <c>[TranslateError]</c> names no static <c>ApiExceptionFactory</c> of the class it is applied to (D8).
    /// </summary>
    public static readonly DiagnosticDescriptor TranslateErrorTargetIsNotAFactory = new(
        id: "BPGW017",
        title: "[TranslateError] must name a static ApiExceptionFactory",
        messageFormat: "[TranslateError(\"{0}\", \"{1}\")] on '{2}' names no static ApiExceptionFactory field or property of '{2}' called '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW018: an attribute of a contributed property that names a type the gateway's assembly cannot access, its own or
    /// one in its arguments, so the emitted property cannot carry it (D9).
    /// </summary>
    /// <remarks>
    /// BPGW007 to BPGW017 are taken by the binding tiers (#66) and forward credentials (#69).
    /// </remarks>
    public static readonly DiagnosticDescriptor InaccessibleValidationAttribute = new(
        id: "BPGW018",
        title: "An exposed property's validation attribute must be accessible to the gateway",
        messageFormat: "Gateway operation '{0}' cannot carry [{1}] from {2}.{3}, because its assembly cannot access that attribute or a type it names, so the gateway would bind, serialize or validate {3} differently from {2}. Make those types public or visible to the gateway's assembly, or declare {3} on '{0}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW019: a required member of the inner operation that the generated forward cannot set (D12, D16).
    /// </summary>
    public static readonly DiagnosticDescriptor RequiredInnerMemberNotSupplied = new(
        id: "BPGW019",
        title: "A forward must set every required member of the inner operation",
        messageFormat: "Gateway operation '{0}' cannot forward to '{1}', which requires {2}, because '{0}' supplies no value of that type: it declares the property with another type, or the member is a required field. Declare it with the inner type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// BPGW020: a property the generated forward copies to the inner operation with no public getter: one the gateway
    /// declares, which the pipeline cannot read, or the inner one, whose default the contributed property cannot start
    /// from (D9, D12).
    /// </summary>
    public static readonly DiagnosticDescriptor ForwardedPropertyNotReadable = new(
        id: "BPGW020",
        title: "A property a forward copies must have a public getter",
        messageFormat: "Gateway operation '{0}' cannot forward {1} to '{2}', because the property has no public getter, on '{0}' or on '{2}', so the generated code cannot read its value or the inner default to start from. Give it a public getter, or declare it on '{0}' with one.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
