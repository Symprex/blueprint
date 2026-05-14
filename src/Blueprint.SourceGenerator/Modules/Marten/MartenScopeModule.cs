using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a Setup render step that sets <c>MartenScopeAccessor.Current</c> to the
/// operation's scoped service provider, enabling the singleton
/// <c>DomainEventDispatchingSessionListener</c> to resolve domain event handlers from the
/// correct DI scope during <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <c>MartenScopeAccessor</c> uses <c>AsyncLocal&lt;T&gt;</c>, so the value
/// is scoped to the current async context and does not leak to other requests. The
/// DI scope owns the lifetime of the service provider, so explicit cleanup is unnecessary.
/// </para>
/// <para>
/// This module is included in the default pipeline configuration and auto-detects Marten
/// availability by checking for <c>Blueprint.Marten.MartenScopeAccessor</c> in the
/// compilation. When Blueprint.Marten is not referenced, the module silently no-ops.
/// This eliminates the need for consumer companion generators to manually compose the module.
/// </para>
/// </remarks>
public sealed class MartenScopeModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string MartenScopeAccessorType = "Blueprint.Marten.MartenScopeAccessor";

    /// <inheritdoc />
    public string ModuleId => nameof(MartenScopeModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

    /// <inheritdoc />
    public string ContributorId => nameof(MartenScopeModule);

    /// <inheritdoc />
    public int Order => 40;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        // Auto-detect: only activate when Blueprint.Marten is referenced in the compilation
        if (context.FindTypeByMetadataName(MartenScopeAccessorType) is null)
        {
            return;
        }

        context.AddRenderStep(PipelineStage.Setup, RenderSetScope);
    }

    private static void RenderSetScope(PipelineRenderContext context)
    {
        context.WriteStatement("global::Blueprint.Marten.MartenScopeAccessor.Current = context.ServiceProvider;");
    }
}
