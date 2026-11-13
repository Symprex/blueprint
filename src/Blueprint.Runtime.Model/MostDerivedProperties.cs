using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Blueprint.Model;

/// <summary>
/// Looks up a type's public instance properties as C# binds them: where a derived type hides a base property with
/// <see langword="new"/>, only the derived one.
/// </summary>
/// <remarks>
/// Reflection hides a base property only when the derived one has the same type. One of a different type, such as a
/// <c>Stream</c> body hiding a <see langword="string"/> (#71), leaves both in <see cref="Type.GetProperties()"/>, and
/// <see cref="Type.GetProperty(string)"/> throws <see cref="AmbiguousMatchException"/> over them.
/// </remarks>
internal static class MostDerivedProperties
{
    /// <summary>
    /// Gets the type's public properties, static ones included as <see cref="Type.GetProperties()"/> gives them, the most-derived declaration of each name.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>One property per name.</returns>
    public static IEnumerable<PropertyInfo> GetAll([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
    {
        return type.GetProperties()
            .GroupBy(static p => p.Name, StringComparer.Ordinal)
            .Select(static g => SelectMostDerived(g));
    }

    /// <summary>
    /// Gets the most-derived public instance property of a name.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="name">The property's name.</param>
    /// <returns>The property, or <see langword="null"/> when the type has none of that name.</returns>
    public static PropertyInfo? Find([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type, string name)
    {
        var matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            .ToList();

        return matches.Count == 0 ? null : SelectMostDerived(matches);
    }

    private static PropertyInfo SelectMostDerived(IEnumerable<PropertyInfo> properties)
    {
        return properties.OrderByDescending(static p => Depth(p.DeclaringType)).First();
    }

    private static int Depth(Type? type)
    {
        var depth = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }
}
