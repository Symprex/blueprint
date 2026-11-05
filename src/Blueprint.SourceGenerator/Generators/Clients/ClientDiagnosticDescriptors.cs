using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Clients;

/// <summary>
/// Diagnostic descriptors emitted by <see cref="TypedClientGenerator"/>, for what a <c>[BlueprintClient]</c> class declares
/// that it cannot complete.
/// </summary>
internal static class ClientDiagnosticDescriptors
{
    private const string Category = "Blueprint.HttpClient";

    private const string HelpLink = "https://github.com/Symprex/blueprint/blob/main/docs/diagnostic-reference.md#typed-http-clients-bpcl";

    /// <summary>
    /// BPCL001: the class cannot be completed by a generated half.
    /// </summary>
    public static readonly DiagnosticDescriptor ClientClassNotSupported = new(
        id: "BPCL001",
        title: "[BlueprintClient] class must be a partial, top-level, non-generic, non-static class",
        messageFormat: "The [BlueprintClient] class '{0}' cannot be completed: it {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL002: the included type is no operation the client can call.
    /// </summary>
    public static readonly DiagnosticDescriptor IncludeNotAnOperation = new(
        id: "BPCL002",
        title: "[BlueprintClientInclude] type must be a concrete, non-generic class implementing IOperation",
        messageFormat: "'{0}' cannot be included in client '{1}': it is not a concrete, non-generic class that implements IOperation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL003: the included operation declares no result.
    /// </summary>
    public static readonly DiagnosticDescriptor IncludeHasNoResult = new(
        id: "BPCL003",
        title: "Included operation declares no result",
        messageFormat: "'{0}' cannot be included in client '{1}': it declares no result, so its method has nothing to return; implement IReturn<T>, directly or through ICommand<T> or IQuery<T>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL004: the included operation has no request builders to build its call from.
    /// </summary>
    public static readonly DiagnosticDescriptor IncludeHasNoRequestBuilders = new(
        id: "BPCL004",
        title: "Included operation has no typed request builder",
        messageFormat: "'{0}' cannot be included in client '{1}': {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL005: the include repeats an operation, or produces a method an earlier include already produced.
    /// </summary>
    public static readonly DiagnosticDescriptor IncludeDuplicated = new(
        id: "BPCL005",
        title: "Operation is included more than once, or two includes produce the same method",
        messageFormat: "'{0}' cannot be included in client '{1}': {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL006: a public generic method cannot be repeated on the generated interface.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericMethodNotSupported = new(
        id: "BPCL006",
        title: "Public method of a [BlueprintClient] class must not be generic",
        messageFormat: "The public method '{0}' of client '{1}' is generic, so it cannot be repeated on the generated interface; make it non-generic or non-public",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL007: the client's serializer context does not cover a type an included operation is called with.
    /// </summary>
    public static readonly DiagnosticDescriptor SerializerContextMissingType = new(
        id: "BPCL007",
        title: "Serializer context does not cover a type an included operation needs",
        messageFormat: "The serializer context '{0}' of client '{1}' does not cover {2}, which calling '{3}' reads or writes; add [JsonSerializable(typeof(...))] for each, or the options' reflection metadata is used for it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL008: no reader reads the included operation's result.
    /// </summary>
    public static readonly DiagnosticDescriptor ResultHasNoReader = new(
        id: "BPCL008",
        title: "Included operation's result is one no response reader supports",
        messageFormat: "'{0}' cannot be included in client '{1}': no response reader supports its result '{2}'; a StatusCodeResult subclass needs a public static Instance singleton, and each member of a Results<T1, T2> needs a reader",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    /// <summary>
    /// BPCL009: a hand-written member conflicts with one the generated half declares.
    /// </summary>
    public static readonly DiagnosticDescriptor MemberConflictsWithGenerated = new(
        id: "BPCL009",
        title: "Member of a [BlueprintClient] class conflicts with a generated member",
        messageFormat: "The member '{0}' of client '{1}' conflicts with a generated member: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);
}
