using Microsoft.CodeAnalysis;

namespace Blueprint.Migration.Analyzers;

/// <summary>
/// Reserves the BPMIG diagnostic range for migration-only analyzers and code fixes.
/// </summary>
public static class MigrationDiagnosticDescriptors
{
    /// <summary>
    /// Gets the diagnostic emitted when legacy Blueprint code still activates OpenAPI explicitly even though HTTP hosts now expose it by default.
    /// </summary>
    public static DiagnosticDescriptor RedundantAddOpenApiActivation { get; } = new(
        id: "BPMIG001",
        title: "Remove redundant AddOpenApi activation",
        messageFormat: "Remove 'AddOpenApi()' because Blueprint HTTP hosts now expose OpenAPI by default.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Legacy Blueprint migrations should remove parameterless AddOpenApi() activation calls because the source-generated HTTP host already implies the OpenAPI surface.");
}
