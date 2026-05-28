using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostic descriptors for persistence compile-time validation.
/// </summary>
internal static class PersistenceDiagnosticDescriptors
{
    private const string Category = "Persistence";

    /// <summary>
    /// BPP001: [DoNotSaveChanges] applied to a query operation where it has no effect.
    /// </summary>
    public static readonly DiagnosticDescriptor DoNotSaveChangesOnNonCommand = new(
        id: "BPP001",
        title: "[DoNotSaveChanges] has no effect on query operations",
        messageFormat: "[DoNotSaveChanges] on '{0}' has no effect because the UnitOfWorkModule only participates for ICommand and IBackgroundTask operations",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The [DoNotSaveChanges] attribute only affects operations that implement ICommand or IBackgroundTask. Remove the attribute from queries where it has no effect.");
}
