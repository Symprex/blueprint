using Microsoft.CodeAnalysis;

namespace Blueprint.Migration.Analyzers;

/// <summary>
/// Reserves the BPMIG diagnostic range for migration-only analyzers and code fixes.
/// </summary>
public static class MigrationDiagnosticDescriptors
{
    /// <summary>
    /// Gets the placeholder descriptor that proves the initial BPMIG scaffold is wired correctly.
    /// </summary>
    public static DiagnosticDescriptor PlaceholderProof { get; } = new(
        id: "BPMIG001",
        title: "Migration analyzer scaffold placeholder",
        messageFormat: "The Blueprint migration analyzer scaffold is wired and may now host real BPMIG diagnostics.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false,
        description: "This placeholder reserves BPMIG001 while the first migration analyzer scaffold is landing.");
}
