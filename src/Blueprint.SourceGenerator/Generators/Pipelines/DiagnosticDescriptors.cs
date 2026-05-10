using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Diagnostic descriptors for the operation executor pipeline source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "OperationExecutorPipelineSourceGenerator";

    // Error diagnostics (BP100-BP199)

    /// <summary>
    /// BP100: No handler found for operation.
    /// </summary>
    public static readonly DiagnosticDescriptor NoHandlerFound = new(
        id: "BP100",
        title: "No handler found for operation",
        messageFormat: "The operation '{0}' does not have a handler method",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Operations must have a handler method to be executed. The handler can be inline (on the operation class) or external (in a separate handler class). Add a method named 'Handle', 'HandleAsync', 'Invoke', or 'InvokeAsync'.");

    /// <summary>
    /// BP101: Multiple handlers found for operation.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleHandlersFound = new(
        id: "BP101",
        title: "Multiple handlers found for operation",
        messageFormat: "The operation '{0}' has multiple handlers: {1}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each operation can have only one handler method. Remove the duplicate handlers or consolidate them into a single method.");

    /// <summary>
    /// BP102: Invalid handler signature.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidHandlerSignature = new(
        id: "BP102",
        title: "Invalid handler signature",
        messageFormat: "The handler method '{0}' has an invalid signature: {1}",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Handler methods must have a valid signature with resolvable parameters.");

    /// <summary>
    /// BP103: Handler parameter cannot be resolved.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvableParameter = new(
        id: "BP103",
        title: "Handler parameter cannot be resolved",
        messageFormat: "The parameter '{0}' of type '{1}' in handler '{2}' cannot be resolved",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Handler parameters must be resolvable from the ApiOperationContext, its properties, or the dependency injection container. Ensure the parameter type is available in ApiOperationContext or registered in DI.");

    /// <summary>
    /// BP104: Handler class must be partial if generating pipeline.
    /// </summary>
    public static readonly DiagnosticDescriptor MustBePartial = new(
        id: "BP104",
        title: "Operation class must be partial",
        messageFormat: "The operation class '{0}' must be partial to support generated pipelines",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Operation classes with inline handlers must be declared as partial to allow the generator to extend them.");

    /// <summary>
    /// BP105: Nullable plain-value handler returns are not supported.
    /// </summary>
    public static readonly DiagnosticDescriptor NullablePlainValueHandlerReturn = new(
        id: "BP105",
        title: "Nullable plain-value handler return is not supported",
        messageFormat: "The handler method '{0}' returns nullable plain value '{1}'. Use an explicit semantic result type or a non-null plain value.",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Plain handler return values are normalized into success results. Nullable plain values are ambiguous, so handlers must either return a non-null plain value or an explicit semantic result type.");

    /// <summary>
    /// BP106: Results union member type is not supported.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedResultsUnionMember = new(
        id: "BP106",
        title: "Results union member is not supported",
        messageFormat: "The Results union member type '{0}' on handler '{1}' is not a supported semantic result shape",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Results<T1, T2> members must be concrete semantic result shapes that the pipeline can normalize without runtime type checks.");

    /// <summary>
    /// BP107: Handler return type is incompatible with the declared operation result contract.
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatibleHandlerReturnContract = new(
        id: "BP107",
        title: "Handler return type is incompatible with the declared operation result contract",
        messageFormat: "The handler method '{0}' returns '{1}', which is not compatible with the declared operation result contract '{2}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Handlers must return a value that the generator can normalize into the declared operation result contract.");

    /// <summary>
    /// BP108: Declared semantic result contract has no enabled-host executor coverage.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingHostExecutorForDeclaredResultContract = new(
        id: "BP108",
        title: "Declared semantic result contract has no enabled-host executor coverage",
        messageFormat: "The operation '{0}' declares semantic result '{1}', but no executor or adapter currently handles it for enabled host '{2}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Host-exposed operations must declare semantic result contracts that the enabled host can translate.");

    /// <summary>
    /// BP109: Declared semantic result union has ambiguous enabled-host translation.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousHostTranslationForDeclaredResultContract = new(
        id: "BP109",
        title: "Declared semantic result union has ambiguous enabled-host translation",
        messageFormat: "The operation '{0}' declares semantic results {1}, which all translate to the same '{2}' response shape for enabled host '{3}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HTTP-exposed results unions must not contain multiple semantic result members that collapse to the same current host response shape.");

    /// <summary>
    /// BP110: HTTP-visible union lacks an explicit same-status discrimination strategy.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingHttpUnionDiscriminationStrategy = new(
        id: "BP110",
        title: "HTTP-visible union lacks an explicit same-status discrimination strategy",
        messageFormat: "The operation '{0}' declares semantic results {1}, which all use HTTP status code '{2}' for enabled host '{3}' without an explicit host/client discrimination strategy",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HTTP-visible unions that share a status code must declare an explicit host/client discrimination strategy unless a dedicated generated exception applies.");

    /// <summary>
    /// BP111: Declared results contract cannot be projected into aligned flattened descriptor outcomes.
    /// </summary>
    public static readonly DiagnosticDescriptor MisalignedFlattenedDescriptorOutcomes = new(
        id: "BP111",
        title: "Declared results contract cannot be projected into aligned flattened descriptor outcomes",
        messageFormat: "The operation '{0}' declares Results member '{1}', which cannot be projected into aligned flattened descriptor outcomes",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Descriptor outcome feature data must stay aligned with semantic result members declared by the operation contract.");

    /// <summary>
    /// BP112: Host-neutral and host-specific no-result shapes are mixed in one HTTP-visible union.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingNoResultShapeCategories = new(
        id: "BP112",
        title: "Host-neutral and host-specific no-result shapes are mixed",
        messageFormat: "The operation '{0}' declares semantic results {1}, which mix host-neutral no-result shapes with host-specific no-content/status-only result families for enabled host '{2}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Results unions must not mix host-neutral NoResultOperationResult with host-specific no-content or status-only result families such as HttpNoContentResult.");

    /// <summary>
    /// BP113: Multiple non-fallback exception translators overlap on the same exception family.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingExceptionTranslatorOwnership = new(
        id: "BP113",
        title: "Exception translator ownership is ambiguous",
        messageFormat: "The exception translators '{0}' for '{1}' and '{2}' for '{3}' overlap. Only the catch-all fallback translator may overlap another translator's exception family.",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pipeline exception translator ownership must stay deterministic so the generated catch shell does not depend on incidental registration order.");

    /// <summary>
    /// BP114: Results union contains the same member type more than once.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateResultsUnionMember = new(
        id: "BP114",
        title: "Duplicate Results union member",
        messageFormat: "The Results union on handler '{1}' contains duplicate member type '{0}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each member type in a Results<T1, T2> union must be unique. Duplicate members are almost certainly a copy-paste mistake.");

    /// <summary>
    /// BP115: Operation implements both ICommand and IQuery.
    /// </summary>
    public static readonly DiagnosticDescriptor CommandAndQueryConflict = new(
        id: "BP115",
        title: "Operation implements both ICommand and IQuery",
        messageFormat: "The operation '{0}' implements both ICommand and IQuery which violates command-query separation — choose one",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An operation must be either a command (mutates state) or a query (reads state), not both. Implementing both ICommand and IQuery causes ambiguous pipeline behavior.");

    // Warning diagnostics (BP200-BP299)

    /// <summary>
    /// BP200: Handler method should be async.
    /// </summary>
    public static readonly DiagnosticDescriptor HandlerShouldBeAsync = new(
        id: "BP200",
        title: "Handler method should be async",
        messageFormat: "The handler method '{0}' should return Task or Task<T> for better performance",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Handler methods should be async to prevent blocking the thread pool and support async operations.");

    /// <summary>
    /// BP201: Consider making inline handler static.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsiderStaticHandler = new(
        id: "BP201",
        title: "Consider making inline handler static",
        messageFormat: "The inline handler '{0}' doesn't use instance state",
        category: Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Static handlers avoid allocating the operation instance when not needed, improving performance. Consider making it static for better performance.");

    /// <summary>
    /// BP202: External handler class not found in current compilation.
    /// </summary>
    public static readonly DiagnosticDescriptor HandlerClassNotFound = new(
        id: "BP202",
        title: "Handler class not accessible",
        messageFormat: "The handler class '{0}' for operation '{1}' could not be found or is not accessible",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Handler classes must be accessible to the generator. Ensure the handler class is public and in the same compilation.");

    // Informational diagnostics (BP300-BP399)

    /// <summary>
    /// BP300: Generated pipeline for operation.
    /// </summary>
    public static readonly DiagnosticDescriptor PipelineGenerated = new(
        id: "BP300",
        title: "Pipeline generated",
        messageFormat: "Generated executor pipeline for operation '{0}' using {1} handler '{2}'",
        category: Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: false, // Disabled by default, can be enabled for debugging
        description: "Information about the generated pipeline for an operation.");
}
