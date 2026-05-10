using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a Population render step that calls registered <c>IPropertyPopulator</c> instances
/// after HTTP binding has completed. This allows consumers to auto-populate operation properties
/// from ambient context (e.g., JWT claims, tenant accessor, correlation state) without writing
/// custom companion generators.
/// <para>
/// The generated code resolves all <c>IPropertyPopulator</c> registrations from DI and calls
/// <c>PopulateAsync</c> on each in registration order. When no populators are registered, the
/// generated code short-circuits with a null check, adding no overhead.
/// </para>
/// <para>
/// This is the compile-time equivalent of Signature365's <c>SetTenantMiddleware</c> pattern
/// where tenant IDs, correlation identifiers, and other ambient state were injected into
/// operation properties via a middleware builder.
/// </para>
/// </summary>
public sealed class PropertyPopulationModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
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
        // Always participate — the generated code gracefully handles zero registrations.
        // PropertyPopulationModule runs at the Population stage after PropertyOwnership
        // has bound HTTP request data, so populators see route/body/query values.
        context.RequireAsync();
        context.AddRenderStep(PipelineStage.Population, RenderPopulation);
    }

    private static void RenderPopulation(PipelineRenderContext context)
    {
        var serviceProvider = context.GetServiceProviderExpression();

        context.WriteBlankLine();
        context.DeclareVariable(
            "propertyPopulators",
            $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<System.Collections.Generic.IEnumerable<Blueprint.Runtime.Middleware.IPropertyPopulator>>({serviceProvider})");

        context.If(
            "propertyPopulators != null",
            branch =>
            {
                branch.ForEach("var populator", "propertyPopulators", foreachBody =>
                {
                    foreachBody.WriteStatement("await populator.PopulateAsync(context, context.OperationCancelled);");
                });
            });
    }
}

