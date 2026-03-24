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

    /// <summary>
    /// Gets the diagnostic emitted when legacy Blueprint code still activates HATEOAS links explicitly even though link generation should be inferred from link metadata.
    /// </summary>
    public static DiagnosticDescriptor RedundantAddHateoasLinksActivation { get; } = new(
        id: "BPMIG002",
        title: "Remove redundant AddHateoasLinks activation",
        messageFormat: "Remove 'AddHateoasLinks()' because link generation should be available whenever Blueprint link metadata exists.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Legacy Blueprint migrations should remove parameterless AddHateoasLinks() activation calls because the source-generated model treats link generation as an inferred capability rather than an activation switch.");

    /// <summary>
    /// Gets the diagnostic emitted when legacy Blueprint code still configures OpenAPI through the old activation overload instead of the future host-level configuration seam.
    /// </summary>
    public static DiagnosticDescriptor ConfiguredAddOpenApiRequiresMigration { get; } = new(
        id: "BPMIG100",
        title: "Migrate configured AddOpenApi activation",
        messageFormat: "Migrate configured 'AddOpenApi(...)' usage to the future 'ConfigureOpenApi(...)' host-level seam because OpenAPI is no longer activated through BlueprintApiBuilder.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint migrations should treat configured AddOpenApi(...) calls as configuration-required work rather than a safe auto-fix because the customization must move to the future host-level ConfigureOpenApi seam.");
}
