using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Auditing;

/// <summary>
/// Contributes <c>auditing.enabled</c> to the per-operation debug manifest, reflecting whether
/// the operation has opted out of audit recording via <c>[DoNotAudit]</c>.
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
    }

    private static bool HasDoNotAuditAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotAuditAttribute"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint.Auditing");
    }
}
