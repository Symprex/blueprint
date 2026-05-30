using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.Helpers;
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
/// settable <c>TenantId</c> property (of any parsable type), the module emits a direct
/// assignment that populates the property from the accessor — no reflection at runtime.
/// </para>
/// <para>
/// Supported property types: <c>string</c>, any type implementing <c>IParsable&lt;T&gt;</c>
/// (e.g. S365's <c>ApiId</c>), any type with a conventional <c>static TryParse</c> method,
/// and types decorated with <c>[TypeConverter]</c>.
/// </para>
/// </summary>
public sealed class TenantModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string TenantAccessorField = "_tenantAccessor";
    private const string TenantResolverField = "_tenantResolver";
    private const string TenantAccessorType = "Blueprint.Tenancy.ITenantAccessor";
    private const string TenantResolverType = "Blueprint.Tenancy.ITenantResolver";
    private const string TenantIdPropertyName = "TenantId";

    private static readonly Regex PlaceholderRegex = new(@"\{\*{0,2}([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

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

        // Tenancy services are cached by OperationExecutorShell base class — no CacheService needed.

        // Detect whether the operation has a settable TenantId property of any supported type.
        var tenantIdInfo = FindSettableTenantIdProperty(context.Operation.TypeSymbol);

        // Store as metadata so the render callback can use it.
        context.AddRenderStep(PipelineStage.Population, ctx => RenderTenantResolution(ctx, tenantIdInfo));
    }

    private static void RenderTenantResolution(PipelineRenderContext context, TenantIdPropertyInfo? tenantIdInfo)
    {
        context.WriteBlankLine();
        context.WriteStatement("// Tenant resolution (compile-time, no reflection, cached services)");

        // Use pre-cached TenantResolver/TenantAccessor from OperationExecutorShell base class
        // instead of per-request DI resolution — eliminates 2 service lookups per request.
        context.WriteStatement(
            "if (this.TenantResolver != null && this.TenantAccessor != null)");
        context.WriteStatement("{");
        context.WriteStatement(
            "    ResolveTenant(this.TenantResolver, this.TenantAccessor, context.UserPrincipal);");
        context.WriteStatement("}");

        // If the operation has a TenantId property, assign directly (no reflection).
        if (tenantIdInfo is not null)
        {
            context.WriteBlankLine();
            context.If(
                "this.TenantAccessor != null",
                ctx =>
                {
                    RenderTenantIdAssignment(ctx, tenantIdInfo);
                });
        }
    }

    /// <summary>
    /// Renders the generated code that assigns the tenant accessor's current value to
    /// <c>operation.TenantId</c>, with type-appropriate conversion and a guard that
    /// prevents overwriting a value already set by route binding.
    /// </summary>
    private static void RenderTenantIdAssignment(PipelineRenderContext ctx, TenantIdPropertyInfo info)
    {
        var accessorCurrent = "this.TenantAccessor.Current";

        switch (info.Kind)
        {
            case TenantIdKind.String:
                // For string TenantId: assign directly, guarded by IsNullOrEmpty.
                ctx.If(
                    $"{accessorCurrent} != null && string.IsNullOrEmpty(operation.TenantId)",
                    innerCtx =>
                    {
                        innerCtx.WriteStatement($"operation.TenantId = {accessorCurrent};");
                    });
                break;

            case TenantIdKind.Parsable:
                {
                    // For parsable value types (e.g. ApiId): use TryParse, guarded by default-value check.
                    var fqType = info.TypeSymbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var tryParseExpr = TypeConversionHelpers.GetTryParseExpression(
                        info.TypeSymbol!, accessorCurrent, "_parsedTenantId");

                    // For Nullable<T>, check HasValue; for value types, compare against default;
                    // for reference types, check null.
                    var defaultCheck = info.IsNullable
                        ? "!operation.TenantId.HasValue"
                        : info.TypeSymbol!.IsValueType
                            ? $"global::System.Collections.Generic.EqualityComparer<{fqType}>.Default.Equals(operation.TenantId, default)"
                            : "operation.TenantId == null";

                    ctx.If(
                        $"{accessorCurrent} != null && {defaultCheck}",
                        innerCtx =>
                        {
                            innerCtx.If(
                                tryParseExpr,
                                parseCtx =>
                                {
                                    parseCtx.WriteStatement("operation.TenantId = _parsedTenantId;");
                                });
                        });
                    break;
                }

            case TenantIdKind.TypeConverterOnly:
                {
                    // For TypeConverter-only types: use exception-based conversion with a try/catch guard.
                    var fqType = info.TypeSymbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var defaultCheck = info.IsNullable
                        ? "!operation.TenantId.HasValue"
                        : info.TypeSymbol!.IsValueType
                            ? $"global::System.Collections.Generic.EqualityComparer<{fqType}>.Default.Equals(operation.TenantId, default)"
                            : "operation.TenantId == null";

                    ctx.If(
                        $"{accessorCurrent} != null && {defaultCheck}",
                        innerCtx =>
                        {
                            innerCtx.WriteStatement("try");
                            innerCtx.WriteStatement("{");
                            innerCtx.WriteStatement(
                                $"    operation.TenantId = ({fqType})System.ComponentModel.TypeDescriptor.GetConverter(typeof({fqType})).ConvertFromInvariantString({accessorCurrent})!;");
                            innerCtx.WriteStatement("}");
                            innerCtx.WriteStatement("catch (System.NotSupportedException) { }");
                        });
                    break;
                }
        }
    }

    /// <summary>
    /// Walks the type hierarchy looking for a settable <c>TenantId</c> property.
    /// Returns information about the first match found, including the property type and
    /// the conversion strategy needed to assign from a string accessor value.
    /// Stops at the first type that declares any <c>TenantId</c> member to avoid shadowing issues.
    /// <para>
    /// Returns <see langword="null"/> when TenantId is already owned by route binding (i.e. the
    /// property name appears in a route template placeholder). PropertyOwnershipModule handles
    /// route-bound properties; the TenantModule only auto-populates non-route TenantId.
    /// </para>
    /// </summary>
    private static TenantIdPropertyInfo? FindSettableTenantIdProperty(INamedTypeSymbol typeSymbol)
    {
        // If TenantId is a route placeholder, PropertyOwnershipModule owns it — skip.
        if (IsTenantIdInRouteTemplate(typeSymbol))
        {
            return null;
        }

        var current = typeSymbol;
        while (current != null)
        {
            var members = current.GetMembers(TenantIdPropertyName);
            var foundAny = false;

            foreach (var member in members)
            {
                foundAny = true;

                if (member is not IPropertySymbol { SetMethod: not null } property)
                {
                    continue;
                }

                // If the property has an explicit HTTP binding attribute ([FromBody], [FromQuery],
                // [FromHeader], [FromCookie]), the developer has opted out of auto-population.
                if (TenantIdConventionHelper.HasExplicitHttpBindingAttribute(property))
                {
                    return null;
                }

                // Unwrap Nullable<T> for value types (e.g. ApiId?) so we can detect
                // the underlying conversion strategy while still handling null tenant IDs.
                var effectiveType = TenantIdConventionHelper.UnwrapNullable(property.Type);
                var isNullable = !SymbolEqualityComparer.Default.Equals(effectiveType, property.Type);

                // String: direct assignment path.
                if (effectiveType.SpecialType == SpecialType.System_String)
                {
                    return new TenantIdPropertyInfo(TenantIdKind.String, null, isNullable);
                }

                // IParsable<T> or static TryParse: compile-time parsable path.
                if (TypeConversionHelpers.IsStringParsable(effectiveType))
                {
                    return new TenantIdPropertyInfo(TenantIdKind.Parsable, effectiveType, isNullable);
                }

                // [TypeConverter]: exception-based conversion path.
                if (TypeConversionHelpers.HasTypeConverterAttribute(effectiveType))
                {
                    return new TenantIdPropertyInfo(TenantIdKind.TypeConverterOnly, effectiveType, isNullable);
                }

                // Unsupported type — we found a TenantId property but can't convert it.
                // Stop searching (don't fall through to base types).
                return null;
            }

            // If this type declares any TenantId member (even non-settable), stop here
            // to respect shadowing — don't look further up the hierarchy.
            if (foundAny)
            {
                return null;
            }

            current = current.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when any route template on the operation contains
    /// a <c>{TenantId}</c> or <c>{*TenantId}</c> placeholder, indicating that route binding
    /// will populate the property and the TenantModule should not override it.
    /// </summary>
    private static bool IsTenantIdInRouteTemplate(INamedTypeSymbol operationType)
    {
        foreach (var url in HttpHostExposureHelper.GetAllRouteTemplates(operationType))
        {
            foreach (Match match in PlaceholderRegex.Matches(url))
            {
                if (string.Equals(match.Groups[1].Value, TenantIdPropertyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Describes a discovered <c>TenantId</c> property's type and the conversion
    /// strategy needed to assign it from the string-valued tenant accessor.
    /// </summary>
    private sealed record TenantIdPropertyInfo(TenantIdKind Kind, ITypeSymbol? TypeSymbol, bool IsNullable = false);

    /// <summary>
    /// Discriminates the code-generation strategy for assigning the tenant accessor's
    /// string value to the operation's <c>TenantId</c> property.
    /// </summary>
    private enum TenantIdKind
    {
        /// <summary>Direct string assignment — <c>operation.TenantId = accessor.Current</c>.</summary>
        String,

        /// <summary>
        /// The type implements <c>IParsable&lt;T&gt;</c> or has a conventional <c>static TryParse</c>.
        /// Generated code uses <c>T.TryParse(accessor.Current, ...)</c>.
        /// </summary>
        Parsable,

        /// <summary>
        /// The type is decorated with <c>[TypeConverter]</c> only. Generated code uses
        /// <c>TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(...)</c>.
        /// </summary>
        TypeConverterOnly,
    }
}
