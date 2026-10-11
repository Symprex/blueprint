using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostic descriptors emitted by the messaging pipeline modules.
/// </summary>
internal static class MessagingDiagnosticDescriptors
{
    /// <summary>
    /// BPM001: <c>[DoNotPublishMessages]</c> applied to an operation that is neither a command nor a background task,
    /// where it has no effect.
    /// </summary>
    public static readonly DiagnosticDescriptor DoNotPublishMessagesOnNonCommand = new(
        id: "BPM001",
        title: "[DoNotPublishMessages] has no effect on operations that are neither commands nor background tasks",
        messageFormat: "[DoNotPublishMessages] on '{0}' has no effect because message-outbox commit is only generated for ICommand and IBackgroundTask operations",
        category: "Blueprint.Messaging",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Symprex/blueprint/blob/main/docs/compile-time-validation.md#BPM001");
}
