using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostic descriptors for background task compile-time validation.
/// </summary>
internal static class BackgroundTaskDiagnosticDescriptors
{
    private const string Category = "BackgroundTasks";

    /// <summary>
    /// BPT001: Background task handler returns a value that will be discarded during background execution.
    /// </summary>
    public static readonly DiagnosticDescriptor TaskHandlerReturnsDiscardedValue = new(
        id: "BPT001",
        title: "Background task handler returns a value that will be discarded",
        messageFormat: "The handler for background task '{0}' returns '{1}', but background tasks run without a consumer for the returned value",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Background task operations execute asynchronously without a response consumer. Return void/Task instead of a value type, or use OperationResult if explicit result signaling is needed.");
}
