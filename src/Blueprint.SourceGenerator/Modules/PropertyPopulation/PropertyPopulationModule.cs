using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a Population render step that calls registered <c>IPropertyPopulator</c> instances
/// after HTTP binding has completed. This allows consumers to auto-populate operation properties
/// from ambient context (e.g., JWT claims, tenant accessor, correlation state) without writing
/// custom companion generators.
/// <para>
/// The generated code resolves all <c>IPropertyPopulator</c> registrations from the root
/// <c>IServiceProvider</c> once at pipeline construction time (property populators are singletons)
/// and invokes <c>PopulateAsync</c> on each in registration order during execution.
/// When no populators are registered, the null check short-circuits with no overhead.
/// </para>
/// </summary>
public sealed class PropertyPopulationModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string FieldName = "_propertyPopulators";
    private const string ServiceType = "Blueprint.Runtime.Middleware.IPropertyPopulator[]";

    /// <inheritdoc />
    public string ModuleId => nameof(PropertyPopulationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(PropertyPopulationModule);

    /// <inheritdoc />
    public int Order => 200;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        // Resolve populators once at construction time — they are registered as singletons.
        // Uses ResolveServiceArray which returns Array.Empty<T>() when none are registered.
        context.CacheService(
            FieldName,
            ServiceType,
            "Blueprint.Runtime.Exec.PipelineHelpers.ResolveServiceArray<Blueprint.Runtime.Middleware.IPropertyPopulator>(serviceProvider)",
            isNullable: false);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.Population, RenderPopulation);
    }

    private static void RenderPopulation(PipelineRenderContext context)
    {
        context.WriteBlankLine();

        // Delegate to shared helper — avoids duplicating the if/else/foreach across every pipeline.
        context.WriteStatement(
            $"await Blueprint.Runtime.Exec.PipelineHelpers.PopulatePropertiesAsync(this.{FieldName}, context).ConfigureAwait(false);");
    }
}

