using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Tenancy;

/// <summary>
/// Symbol helpers <see cref="TenantModule"/> uses to decide how, and whether, it fills an operation's <c>TenantId</c>.
/// </summary>
/// <remarks>
/// A <c>TenantId</c> is not owned by the tenant: <c>PropertyOwnershipModule</c> binds it from the request (route, query or
/// body) like any other property and publishes it in the OpenAPI document, as legacy Blueprint did.
/// <see cref="TenantModule"/> then fills it only when the request left it unset.
/// </remarks>
internal static class TenantIdConventionHelper
{
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
