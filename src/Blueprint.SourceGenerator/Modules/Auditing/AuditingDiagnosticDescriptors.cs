using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Auditing;

/// <summary>
/// Diagnostic descriptors for auditing compile-time validation. The BPA prefix groups
/// Blueprint Auditing diagnostics together in IDE and CI output.
/// </summary>
internal static class AuditingDiagnosticDescriptors
{
    private const string Category = "Auditing";

    /// <summary>
    /// BPA001: [Sensitive] applied to a non-public property where it has no effect because
    /// the descriptor only publishes public properties to the audit metadata surface.
    /// </summary>
    public static readonly DiagnosticDescriptor SensitiveOnNonPublicProperty = new(
        id: "BPA001",
        title: "[Sensitive] has no effect on non-public properties",
        messageFormat: "[Sensitive] on '{0}.{1}' has no effect because only public properties are included in audit metadata",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The [Sensitive] attribute only affects public properties that appear in the operation's audit metadata surface. Remove the attribute from non-public properties where it has no observable effect.");
}
