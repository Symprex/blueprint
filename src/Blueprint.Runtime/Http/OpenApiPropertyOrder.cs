using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Gives the order legacy Blueprint's document listed the properties of a type in, which is not the order of the CLR
/// properties' names and is not the order reflection returns them in.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's NJsonSchema listed the properties of a type as Newtonsoft.Json's contract did: those each base type declares,
/// from the nearest base to the root, and then the type's own, each in declaration order. A property a base type declares
/// virtual and a derived type overrides keeps the position of the declaration, so <c>ApiResource.Object</c> is among
/// <c>ApiResource</c>'s whichever type overrides it; a property that hides one of a base type's takes the hiding type's
/// position.
/// </para>
/// <para>
/// It was established against the declaration order of the C# type behind each of the 518 components of Signature365's
/// 18 legacy documents (<c>8b40ecc961</c>) that could be matched to one and had two or more properties in common: 486 have
/// no base type with properties to tell the rules apart, 29 do and fit only this, and three are unmatched (a lowercase
/// member in one type, and two where the type matched is not the one documented). Listing each type's own properties
/// before its bases' fits none of the 29, and the root's first, the bases' from the root down, misses the three
/// <c>Paged*</c> and <c>TenantListOf*</c> resources among them: <c>values,total,$object,$links,$partial,pageSize,currentPage</c>
/// against <c>$object,$links,$partial,values,total</c> for a plain <c>ListOf*</c>.
/// </para>
/// </remarks>
internal static class OpenApiPropertyOrder
{
    /// <summary>
    /// Returns the names of the public instance properties of <paramref name="type"/> in legacy's order.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The CLR property names, each once.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Public properties of a covered body type and its base types are preserved with the serializer context.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Public properties of a covered body type and its base types are preserved with the serializer context.")]
    public static IReadOnlyList<string> GetLegacyOrder(Type type)
    {
        var declaringTypes = new List<Type>();

        for (var baseType = type.BaseType; baseType is not null && baseType != typeof(object); baseType = baseType.BaseType)
        {
            declaringTypes.Add(baseType);
        }

        declaringTypes.Add(type);

        var names = new List<string>();

        foreach (var declaringType in declaringTypes)
        {
            foreach (var property in declaringType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                // An override is listed where the property was first declared, which is a type the walk has already
                // reached, or will, and not again here.
                var accessor = property.GetMethod ?? property.SetMethod;

                if (accessor is not null && accessor.GetBaseDefinition().DeclaringType != declaringType)
                {
                    continue;
                }

                // A property hiding another of the same name takes the hiding type's position.
                names.Remove(property.Name);
                names.Add(property.Name);
            }
        }

        return names;
    }
}
