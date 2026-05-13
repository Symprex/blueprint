using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Tenancy;

/// <summary>
/// First-class tenant support module that generates compile-time property population for
/// operations containing a <c>TenantId</c> property, completely eliminating the need for
/// runtime <c>IPropertyPopulator</c> and reflection for the tenancy case.
/// <para>
/// For every operation, the module resolves the tenant ID from the authenticated user's claims
/// via <c>Blueprint.Tenancy.ITenantResolver</c> and stores it in
/// <c>Blueprint.Tenancy.ITenantAccessor</c>. When the operation additionally declares a
/// settable <c>string TenantId</c> property, the module emits a direct assignment that populates
/// the property from the accessor — no <c>GetProperty</c>/<c>SetValue</c> reflection at runtime.
/// </para>
/// </summary>
public sealed class TenantModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string TenantAccessorField = "_tenantAccessor";
    private const string TenantResolverField = "_tenantResolver";
    private const string TenantAccessorType = "Blueprint.Tenancy.ITenantAccessor";
    private const string TenantResolverType = "Blueprint.Tenancy.ITenantResolver";
    private const string TenantIdPropertyName = "TenantId";

    /// <inheritdoc />
    public string ModuleId => nameof(TenantModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(TenantModule);

    /// <inheritdoc />
    /// <remarks>
    /// Runs during Population, after body binding (order 100) and before general property
    /// populators (order 200). This ensures TenantId is set before any consumer-provided
    /// populators that might depend on it.
    /// </remarks>
    public int Order => 150;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        // Only participate when the consumer references the tenancy contracts.
        // If ITenantAccessor/ITenantResolver are not in the compilation, the consumer
        // hasn't opted into first-class tenancy, so we skip silently.
        var tenantAccessorSymbol = context.FindTypeByMetadataName(TenantAccessorType);
        var tenantResolverSymbol = context.FindTypeByMetadataName(TenantResolverType);

        if (tenantAccessorSymbol is null || tenantResolverSymbol is null)
        {
            return;
        }

        // Cache both services at pipeline construction time (singletons).
        context.CacheService(
            TenantAccessorField,
            TenantAccessorType,
            $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{TenantAccessorType}>(serviceProvider)",
            isNullable: true);

        context.CacheService(
            TenantResolverField,
            TenantResolverType,
            $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{TenantResolverType}>(serviceProvider)",
            isNullable: true);

        // Detect whether the operation has a settable string TenantId property.
        var hasTenantIdProperty = HasSettableTenantIdProperty(context.Operation.TypeSymbol);

        // Store as metadata so the render callback can use it.
        context.AddRenderStep(PipelineStage.Population, ctx => RenderTenantResolution(ctx, hasTenantIdProperty));
    }

    private static void RenderTenantResolution(PipelineRenderContext context, bool hasTenantIdProperty)
    {
        context.WriteBlankLine();
        context.WriteStatement("// Tenant resolution (compile-time, no reflection)");

        // Guard: only run when both services are registered.
        context.If(
            $"this.{TenantResolverField} != null && this.{TenantAccessorField} != null",
            ctx =>
            {
                // Resolve tenant from claims and set the accessor via shared helper.
                ctx.WriteStatement(
                    $"Blueprint.Runtime.Exec.PipelineHelpers.ResolveTenant(this.{TenantResolverField}, this.{TenantAccessorField}, context.UserPrincipal);");

                // If the operation has a TenantId property, assign directly (no reflection).
                if (hasTenantIdProperty)
                {
                    ctx.WriteBlankLine();
                    ctx.If(
                        $"this.{TenantAccessorField}.Current != null && string.IsNullOrEmpty(operation.TenantId)",
                        innerCtx =>
                        {
                            innerCtx.WriteStatement($"operation.TenantId = this.{TenantAccessorField}.Current;");
                        });
                }
            });
    }

    /// <summary>
    /// Checks whether the given type symbol has a settable <c>string TenantId</c> property.
    /// Walks the type hierarchy since the property may be declared on a base type.
    /// </summary>
    private static bool HasSettableTenantIdProperty(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current != null)
        {
            foreach (var member in current.GetMembers(TenantIdPropertyName))
            {
                if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_String, SetMethod: not null })
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }
}
