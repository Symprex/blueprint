using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Tenancy;

/// <summary>
/// Shared decision logic for whether a <c>TenantId</c> property will be auto-populated by
/// <see cref="TenantModule"/> at runtime, used by both <see cref="TenantModule"/> (pipeline
/// generation) and <c>PropertyOwnershipModule</c> (descriptor/OpenAPI generation) so that
/// spec visibility and runtime behaviour stay in sync.
/// </summary>
/// <remarks>
/// <para>
/// A <c>TenantId</c> property is considered tenant-auto-populated when ALL of:
/// <list type="number">
/// <item>The consumer references both <c>Blueprint.Tenancy.ITenantAccessor</c> and
/// <c>Blueprint.Tenancy.ITenantResolver</c> (tenancy opt-in).</item>
/// <item>The property is named exactly <c>TenantId</c> (case-sensitive, matching existing conventions).</item>
/// <item>The property is settable (<c>set</c> accessor exists).</item>
/// <item>The property type is convertible from a string (string, IParsable, TryParse, or TypeConverter).</item>
/// <item>The property is NOT bound from a route placeholder (<c>{TenantId}</c> in a Link/SelfLink URL).</item>
/// <item>The property does NOT have an explicit HTTP binding attribute (<c>[FromBody]</c>,
/// <c>[FromQuery]</c>, <c>[FromHeader]</c>, <c>[FromCookie]</c>).</item>
/// </list>
/// </para>
/// </remarks>
internal static class TenantIdConventionHelper
{
    /// <summary>
    /// The well-known property name that triggers tenant auto-population.
    /// </summary>
    internal const string TenantIdPropertyName = "TenantId";

    private const string TenantAccessorType = "Blueprint.Tenancy.ITenantAccessor";
    private const string TenantResolverType = "Blueprint.Tenancy.ITenantResolver";

    /// <summary>
    /// Returns <see langword="true"/> when the compilation references both tenancy contract types,
    /// meaning the consumer has opted into first-class tenant support.
    /// </summary>
    /// <param name="compilation">The Roslyn compilation to search.</param>
    /// <returns><see langword="true"/> when tenancy is enabled.</returns>
    internal static bool IsTenancyEnabled(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName(TenantAccessorType) is not null
            && compilation.GetTypeByMetadataName(TenantResolverType) is not null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when both tenancy contract types are resolvable via
    /// the given type lookup (typically backed by an operation lookup).
    /// </summary>
    /// <param name="findTypeByMetadataName">A delegate that resolves types by metadata name.</param>
    /// <returns><see langword="true"/> when tenancy is enabled.</returns>
    internal static bool IsTenancyEnabled(Func<string, INamedTypeSymbol?> findTypeByMetadataName)
    {
        return findTypeByMetadataName(TenantAccessorType) is not null
            && findTypeByMetadataName(TenantResolverType) is not null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the given property will be auto-populated by
    /// <see cref="TenantModule"/> at runtime — meaning it should be excluded from
    /// OpenAPI parameters/body and should not receive HTTP binding code.
    /// </summary>
    /// <param name="property">The property symbol to evaluate.</param>
    /// <param name="routePlaceholders">The set of route placeholder names from the operation's Link/SelfLink attributes.</param>
    /// <param name="hasExplicitHttpBinding">
    /// Whether the property has any explicit HTTP binding attribute ([FromBody], [FromQuery], [FromHeader], [FromCookie]).
    /// When true, the explicit binding takes precedence and tenant auto-population is suppressed.
    /// </param>
    /// <returns><see langword="true"/> when the property will be tenant-auto-populated.</returns>
    internal static bool IsTenantAutoPopulated(
        IPropertySymbol property,
        IReadOnlyCollection<string> routePlaceholders,
        bool hasExplicitHttpBinding)
    {
        // Name must be exactly TenantId (case-sensitive, matching existing conventions).
        if (property.Name != TenantIdPropertyName)
        {
            return false;
        }

        // Must be settable.
        if (property.SetMethod is null)
        {
            return false;
        }

        // Explicit HTTP binding takes precedence — if the user wrote [FromBody] on TenantId,
        // they intend it to come from the request, not from the tenant accessor.
        if (hasExplicitHttpBinding)
        {
            return false;
        }

        // Route-bound TenantId is populated by route binding, not tenant auto-population.
        if (routePlaceholders.Contains(TenantIdPropertyName))
        {
            return false;
        }

        // The property type must be convertible from the string-valued tenant accessor.
        var propType = property.Type;

        // Unwrap Nullable<T> for value types.
        if (propType is INamedTypeSymbol { IsValueType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            propType = nullable.TypeArguments[0];
        }

        if (propType.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        if (TypeConversionHelpers.IsStringParsable(propType))
        {
            return true;
        }

        if (TypeConversionHelpers.HasTypeConverterAttribute(propType))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the property has any explicit HTTP binding attribute
    /// (<c>[FromBody]</c>, <c>[FromQuery]</c>, <c>[FromHeader]</c>, <c>[FromCookie]</c>).
    /// </summary>
    /// <param name="property">The property symbol to inspect.</param>
    /// <returns><see langword="true"/> when an explicit HTTP binding attribute is present.</returns>
    internal static bool HasExplicitHttpBindingAttribute(IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name is "FromBodyAttribute" or "FromQueryAttribute" or "FromHeaderAttribute" or "FromCookieAttribute")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Unwraps <c>Nullable&lt;T&gt;</c> for value types, returning the underlying type argument.
    /// Returns the original type unchanged for reference types or non-nullable value types.
    /// </summary>
    /// <param name="type">The type symbol to unwrap.</param>
    /// <returns>The underlying type if nullable, otherwise the original type.</returns>
    internal static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsValueType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            return nullable.TypeArguments[0];
        }

        return type;
    }
}
