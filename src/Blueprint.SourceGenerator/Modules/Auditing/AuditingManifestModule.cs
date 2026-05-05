using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules.Auditing;

/// <summary>
/// Contributes <c>auditing.enabled</c> and <c>trace.events</c> to the per-operation debug manifest,
/// reflecting whether the operation has opted out of audit recording via <c>[DoNotAudit]</c> and
/// documenting the Activity events that auditing will emit at runtime.
/// </summary>
/// <remarks>
/// This is a manifest-only module separated from <see cref="AuditingModule"/> because the pipeline
/// contributor has a dependency on <c>HandlerExecutionModule</c> that must not propagate to the
/// debug-manifest capability where ordering is irrelevant.
/// </remarks>
internal sealed class AuditingManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(AuditingManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(AuditingManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var hasDoNotAudit = HasDoNotAuditAttribute(context.GetOperationTypeSymbol());
        context.AddEntry("auditing.enabled", (!hasDoNotAudit).ToString());

        if (!hasDoNotAudit)
        {
            context.AddEntry("trace.events", BuildAuditEventCatalog());
        }
    }

    private static JArray BuildAuditEventCatalog()
    {
        return new JArray
        {
            new JObject
            {
                ["name"] = "blueprint.audit.started",
                ["tags"] = new JArray { "blueprint.operation.type" },
            },
            new JObject
            {
                ["name"] = "blueprint.audit.succeeded",
                ["tags"] = new JArray { "blueprint.operation.type" },
            },
            new JObject
            {
                ["name"] = "blueprint.audit.failed",
                ["tags"] = new JArray { "blueprint.operation.type", "exception.type", "exception.message", "exception.stacktrace" },
            },
        };
    }

    private static bool HasDoNotAuditAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotAuditAttribute"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Auditing");
    }
}
