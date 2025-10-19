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
        messageFormat: "The operation '{0}' does not have a handler method. Add a method named 'Handle', 'HandleAsync', 'Invoke', or 'InvokeAsync' either on the operation class or in a separate handler class.",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Operations must have a handler method to be executed. The handler can be inline (on the operation class) or external (in a separate handler class).");

    /// <summary>
    /// BP101: Multiple handlers found for operation.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleHandlersFound = new(
        id: "BP101",
        title: "Multiple handlers found for operation",
        messageFormat: "The operation '{0}' has multiple handlers. Only one handler method is allowed per operation. Found handlers: {1}",
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
        messageFormat: "The parameter '{0}' of type '{1}' in handler '{2}' cannot be resolved. Ensure it's available in ApiOperationContext or registered in DI.",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Handler parameters must be resolvable from the ApiOperationContext, its properties, or the dependency injection container.");

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

    // Warning diagnostics (BP200-BP299)

    /// <summary>
    /// BP200: Handler method should be async.
    /// </summary>
    public static readonly DiagnosticDescriptor HandlerShouldBeAsync = new(
        id: "BP200",
        title: "Handler method should be async",
        messageFormat: "The handler method '{0}' should return Task or Task<T> for better performance and to avoid blocking operations",
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
        messageFormat: "The inline handler '{0}' doesn't use instance state. Consider making it static for better performance",
        category: Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Static handlers avoid allocating the operation instance when not needed, improving performance.");

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
