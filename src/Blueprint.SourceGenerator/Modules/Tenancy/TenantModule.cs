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
/// <c>TenantId</c> property with a public, non-init setter, of a supported type, the module emits a direct
/// assignment that populates the property from the accessor — no reflection at runtime.
/// </para>
/// <para>
/// The property is bound from the request first, like any other (<c>PropertyOwnershipModule</c>), and is filled only
/// when the caller is authenticated and the request left it unset, as legacy's <c>SetTenantMiddleware</c> did. A tenant
/// the caller names is therefore what the operation's authorisers see and must refuse if the caller may not use it. An
/// operation whose <c>TenantId</c> must come from the caller's claims alone marks it <c>[FromAuthContext]</c>.
/// </para>
/// <para>
/// Supported property types: <c>string</c>, any type implementing <c>IParsable&lt;T&gt;</c>,
/// any type with a conventional <c>static TryParse</c> method, and types decorated with <c>[TypeConverter]</c>
/// (e.g. S365's <c>ApiId</c>). An app with <c>[BlueprintAppTenancy(TenantIdType = typeof(T))]</c> has only a
/// <c>TenantId</c> of type <c>T</c> or <c>T?</c> filled, as legacy's <c>SetTenantMiddleware</c> filled only an
/// <c>ApiId</c> or <c>ApiId?</c> (<c>SetTenantMiddleware.cs:65</c>). Whatever the app declares, an init-only
/// <c>TenantId</c> is never filled, as legacy skipped one (<c>:65</c>, <c>:77-90</c>), nor one whose setter is not
/// public. Legacy's generated assignment could not have compiled against a private or protected setter, and against an
/// internal one only from the operation's own assembly; a public setter is what <c>HandlerDiscovery</c> and the typed
/// HTTP client require of a settable property too. A <c>[FromAuthContext]</c> <c>TenantId</c> without a <c>ClaimType</c>
/// is filled by the same rule, so in an app that declares another type it is never set.
/// </para>
/// </summary>
public sealed class TenantModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string TenantAccessorField = "_tenantAccessor";
    private const string TenantResolverField = "_tenantResolver";
    private const string TenantAccessorType = "Blueprint.Tenancy.ITenantAccessor";
    private const string TenantResolverType = "Blueprint.Tenancy.ITenantResolver";
    private const string TenantIdPropertyName = "TenantId";

    private static readonly Regex PlaceholderRegex = RoutePlaceholderSyntax.Placeholder;

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

        // Detect whether the operation has a settable TenantId property of a type the app has filled.
        var tenantIdInfo = FindSettableTenantIdProperty(context.Operation.TypeSymbol, context.TenantIdTypeName);

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

        // If the operation has a TenantId property, assign directly (no reflection). Legacy's SetTenantMiddleware
        // (Signature365 8b40ecc961, Common/Api/SetTenantMiddleware.cs:60-74) filled it for an authenticated caller only,
        // and only when the request had left it unset, so that a tenant the caller named reached the authorisers.
        if (tenantIdInfo is not null)
        {
            context.WriteBlankLine();
            context.If(
                "this.TenantAccessor != null && context.UserPrincipal?.Identity?.IsAuthenticated == true",
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
                    // For TypeConverter-only types: use exception-based conversion with a try/catch guard. The converter
                    // is given the caller's tenant even when there is none, as legacy's ApiId.Hydrate(currentTenant) was
                    // (SetTenantMiddleware.cs:68): Signature365's ApiId converter throws ArgumentOutOfRangeException for it
                    // (Common.Core/Data/ApiId.cs:116-126), which is not caught, so an authenticated caller with no tenant
                    // that names none fails as it did. A converter that cannot convert null throws NotSupportedException.
                    var fqType = info.TypeSymbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var defaultCheck = info.IsNullable
                        ? "!operation.TenantId.HasValue"
                        : info.TypeSymbol!.IsValueType
                            ? $"global::System.Collections.Generic.EqualityComparer<{fqType}>.Default.Equals(operation.TenantId, default)"
                            : "operation.TenantId == null";

                    ctx.If(
                        defaultCheck,
                        innerCtx =>
                        {
                            innerCtx.WriteStatement("try");
                            innerCtx.WriteStatement("{");
                            innerCtx.WriteStatement(
                                $"    operation.TenantId = ({fqType})System.ComponentModel.TypeDescriptor.GetConverter(typeof({fqType})).ConvertFromInvariantString({accessorCurrent}!)!;");
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
    /// <para>
    /// Returns <see langword="null"/> too for a <c>TenantId</c> whose setter is init-only or not public, and, when
    /// <paramref name="tenantIdTypeName"/> is given, for one whose type (a <c>Nullable&lt;T&gt;</c> read as its
    /// <c>T</c>) is not that type. Such a property is never filled.
    /// </para>
    /// </summary>
    /// <param name="typeSymbol">The operation type.</param>
    /// <param name="tenantIdTypeName">The fully qualified name of the app's declared <c>TenantId</c> type, if any.</param>
    private static TenantIdPropertyInfo? FindSettableTenantIdProperty(INamedTypeSymbol typeSymbol, string? tenantIdTypeName)
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

                // Legacy filled only a TenantId that CanWrite and was not init-only (SetTenantMiddleware.cs:65, :77-90),
                // and its generated assignment could not have compiled against a private or protected setter.
                if (member is not IPropertySymbol { SetMethod: { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false } } property)
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

                // An app that declares its TenantId type has only that type, or its Nullable, filled, as legacy filled
                // only an ApiId or ApiId? (SetTenantMiddleware.cs:65).
                if (tenantIdTypeName is not null
                    && !string.Equals(effectiveType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), tenantIdTypeName, StringComparison.Ordinal))
                {
                    return null;
                }

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
    /// Returns whether <c>TenantId</c> is exclusively route-owned — a placeholder in <em>every</em> one of the
    /// operation's route templates, legacy's "every link" ownership rule
    /// (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/MessagePopulation/HttpRouteMessagePopulationSource.cs:33-40</c>).
    /// A <c>TenantId</c> that is a placeholder in only some links is bound from the route when present and falls back to
    /// the query string or body otherwise (<c>PropertyOwnershipModule</c>'s own "every link" handling); this module's
    /// fill-when-unset guard still applies to it when neither source sets it, as legacy's own unconditional
    /// <c>SetTenantMiddleware</c> fill did.
    /// </summary>
    private static bool IsTenantIdInRouteTemplate(INamedTypeSymbol operationType)
    {
        var sawAny = false;

        foreach (var url in HttpHostExposureHelper.GetAllRouteTemplates(operationType))
        {
            sawAny = true;
            var hasPlaceholder = false;

            foreach (Match match in PlaceholderRegex.Matches(url))
            {
                if (string.Equals(match.Groups[1].Value, TenantIdPropertyName, StringComparison.Ordinal))
                {
                    hasPlaceholder = true;
                    break;
                }
            }

            if (!hasPlaceholder)
            {
                return false;
            }
        }

        return sawAny;
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
