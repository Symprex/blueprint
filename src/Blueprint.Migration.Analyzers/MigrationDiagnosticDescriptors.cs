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
    /// Gets the diagnostic emitted when legacy Blueprint code still activates authorisation explicitly even though auth stages should be inferred from declared requirements.
    /// </summary>
    public static DiagnosticDescriptor RedundantAddAuthorisationActivation { get; } = new(
        id: "BPMIG003",
        title: "Remove redundant AddAuthorisation activation",
        messageFormat: "Remove 'AddAuthorisation()' because generated auth stages should be available whenever operations declare authorisation requirements.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Legacy Blueprint migrations should remove parameterless AddAuthorisation() activation calls because the target architecture treats authorisation as an inferred capability rather than a feature toggle.");

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

    /// <summary>
    /// Gets the diagnostic emitted when legacy Blueprint code still configures authentication through the old activation overload instead of an explicit migration-time configuration seam.
    /// </summary>
    public static DiagnosticDescriptor ConfiguredAddAuthenticationRequiresMigration { get; } = new(
        id: "BPMIG101",
        title: "Migrate configured AddAuthentication activation",
        messageFormat: "Migrate configured 'AddAuthentication(...)' usage through the new authentication configuration path because auth stages are no longer activated through BlueprintApiBuilder feature toggles.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint migrations should treat configured AddAuthentication(...) calls as configuration-required work because context loading and consumer-owned authentication registration still need an explicit migration seam.");

    /// <summary>
    /// Gets the diagnostic emitted when legacy Blueprint operations still rely on FromResource metadata that must move into the source-generated auth/resource model.
    /// </summary>
    public static DiagnosticDescriptor LegacyFromResourceUsageRequiresMigration { get; } = new(
        id: "BPMIG200",
        title: "Migrate FromResource usage",
        messageFormat: "Migrate 'FromResource(...)' usage because auth/resource inheritance must move into Blueprint's source-generated metadata model.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint migrations should treat FromResource(...) usage as consumer-extension rewrite work because the replacement auth/resource model needs explicit source-generated metadata instead of runtime attribute discovery.");

    /// <summary>
    /// Gets the diagnostic emitted when legacy Blueprint resource templates still use raw auth placeholders that must move into generated auth metadata bindings.
    /// </summary>
    public static DiagnosticDescriptor LegacyAuthPlaceholderUsageRequiresMigration { get; } = new(
        id: "BPMIG201",
        title: "Migrate auth placeholder usage",
        messageFormat: "Migrate '{Auth:...}' placeholder usage because auth-template expansion must move into Blueprint's source-generated metadata model.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint migrations should treat raw {Auth:...} placeholder templates as consumer-extension rewrite work because the replacement auth/resource model needs explicit generated placeholder bindings instead of runtime string expansion.");

    /// <summary>
    /// Gets the diagnostic emitted when an abstract generic class directly implements <c>IQuery&lt;T&gt;</c> or
    /// <c>ICommand&lt;T&gt;</c>. This pattern is now fully supported — <c>IOperation</c> no longer declares a
    /// <c>static abstract Configure()</c> member. The diagnostic is retained (disabled by default, informational
    /// severity) for migration audit purposes only.
    /// </summary>
    public static DiagnosticDescriptor AbstractGenericBaseImplementsOperation { get; } = new(
        id: "BPMIG202",
        title: "Abstract generic base implements IQuery/ICommand (informational — now supported)",
        messageFormat: "Abstract generic class '{0}' implements '{1}'. Blueprint now supports this pattern; the source generator will generate Configure() on concrete subclasses only.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false,
        description: "Abstract generic bases implementing IQuery<T>/ICommand are now supported because IOperation no longer declares static abstract Configure(). This diagnostic is retained for informational purposes only.");

    /// <summary>
    /// Gets the diagnostic emitted when a using directive references a legacy Blueprint namespace that has been renamed or reorganised.
    /// </summary>
    public static DiagnosticDescriptor LegacyNamespaceUsing { get; } = new(
        id: "BPMIG300",
        title: "Update legacy Blueprint namespace",
        messageFormat: "Replace 'using {0};' with 'using {1};' for the source-generated Blueprint framework.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint namespaces have been reorganised in the source-generated framework. Update using directives to the new namespace locations.");

    /// <summary>
    /// Gets the diagnostic emitted when a using directive references a legacy Blueprint namespace that has been
    /// removed entirely (e.g. compile-time code-generation namespaces like <c>Blueprint.Compiler</c>).
    /// </summary>
    public static DiagnosticDescriptor LegacyNamespaceRemoved { get; } = new(
        id: "BPMIG303",
        title: "Remove obsolete Blueprint namespace",
        messageFormat: "Remove 'using {0};' because this namespace has no equivalent in the source-generated Blueprint framework. Code-generation is now handled entirely at compile time by the source generator.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint compile-time namespaces (Blueprint.Compiler, Blueprint.CodeGen) have no runtime equivalent in the source-generated framework. Remove the using directive and migrate any dependent code to source-generator modules or ASP.NET Core middleware.");

    /// <summary>
    /// Gets the diagnostic emitted when code implements or references <c>IMiddlewareBuilder</c>,
    /// which has been replaced by the source-generator module system.
    /// </summary>
    public static DiagnosticDescriptor LegacyMiddlewareBuilderUsage { get; } = new(
        id: "BPMIG301",
        title: "IMiddlewareBuilder replaced by source-generator modules",
        messageFormat: "'{0}' implements IMiddlewareBuilder, which is replaced by IBlueprintSourceGeneratorModule in the source-generated framework. See the consumer migration guide for companion generator patterns.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint's IMiddlewareBuilder compile-time middleware pattern has been replaced by IBlueprintSourceGeneratorModule. Custom middleware should be migrated to companion generator packages or runtime ASP.NET Core middleware.");

    /// <summary>
    /// Gets the diagnostic emitted when code references <c>BlueprintApiBuilder</c>,
    /// which has been replaced by <c>AddBlueprintApi&lt;TApp, TExec&gt;</c> and <c>BlueprintServiceBuilder</c>.
    /// </summary>
    public static DiagnosticDescriptor LegacyBlueprintApiBuilderUsage { get; } = new(
        id: "BPMIG302",
        title: "BlueprintApiBuilder replaced by AddBlueprintApi<TApp, TExec>",
        messageFormat: "'{0}' references BlueprintApiBuilder, which is replaced by 'AddBlueprintApi<TApp, TExec>(...)' with BlueprintServiceBuilder in the source-generated framework.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Legacy Blueprint's BlueprintApiBuilder configuration pattern has been replaced by the source-generated AddBlueprintApi<TApp, TExec>() registration and BlueprintServiceBuilder for infrastructure services.");

    /// <summary>
    /// Gets the diagnostic emitted when an operation class implementing <c>ICommand&lt;T&gt;</c> or
    /// <c>IQuery&lt;T&gt;</c> is missing the <c>partial</c> keyword required by the source generator.
    /// </summary>
    public static DiagnosticDescriptor OperationMissingPartialKeyword { get; } = new(
        id: "BPMIG400",
        title: "Add 'partial' to operation class",
        messageFormat: "Operation '{0}' implements '{1}' but is not declared as 'partial'. Blueprint source generators require the 'partial' keyword to emit descriptors and pipeline code.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blueprint source generators emit partial class extensions for operation types (descriptors, Configure() methods). Classes implementing ICommand<T> or IQuery<T> must be declared 'partial' for the generated code to compile.");

    /// <summary>
    /// Gets the diagnostic emitted when an API resource class inheriting <c>ApiResource</c> is missing
    /// the <c>partial</c> keyword. The source generator emits the <c>Object</c> property override on a
    /// partial extension, so the original declaration must be <c>partial</c>.
    /// </summary>
    public static DiagnosticDescriptor ResourceMissingPartialKeyword { get; } = new(
        id: "BPMIG401",
        title: "Add 'partial' to API resource class",
        messageFormat: "Resource class '{0}' inherits from ApiResource but is not declared as 'partial'. Blueprint source generators require the 'partial' keyword to emit the Object property override.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blueprint source generators emit a partial class extension with the Object property override for API resource types. Classes inheriting from ApiResource must be declared 'partial' for the generated code to compile.");

    /// <summary>
    /// Gets the diagnostic emitted when a file uses Blueprint types that have been relocated
    /// from the root <c>Blueprint</c> namespace to a sub-namespace in the source-generated
    /// Blueprint, and the required <c>using</c> directive is missing.
    /// </summary>
    public static DiagnosticDescriptor RelocatedTypeNeedUsing { get; } = new(
        id: "BPMIG402",
        title: "Add missing using for relocated Blueprint type",
        messageFormat: "This file uses Blueprint types that moved to '{0}'. Add 'using {0};' to resolve them.",
        category: "BlueprintMigration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "In source-generated Blueprint, several types moved from the root 'Blueprint' namespace to sub-namespaces (e.g. ApiOperationContext → Blueprint.Runtime.Exec, ApiOperationDescriptor → Blueprint.Model). Add the required using directive.");
}
