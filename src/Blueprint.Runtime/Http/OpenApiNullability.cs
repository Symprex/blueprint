using System.Reflection;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Decides whether a property's type is not nullable, as legacy Blueprint's required rule
/// (<see cref="OpenApiRequiredProperties.NonNullable"/>) decided it.
/// </summary>
internal static class OpenApiNullability
{
    /// <summary>
    /// Returns whether <paramref name="property"/>'s type is not nullable: a value type that is not
    /// <see cref="Nullable{T}"/>, or a reference type annotated as not nullable, as Namotion.Reflection's
    /// <c>Nullability.NotNullable</c> decided it for legacy. A reference type with no annotation is not known to be
    /// either, and so is not counted.
    /// </summary>
    /// <param name="property">The CLR property.</param>
    /// <param name="nullabilityContext">The context that reads the property's nullable annotations.</param>
    /// <returns><see langword="true"/> when the property's type is not nullable.</returns>
    public static bool IsNotNullable(PropertyInfo property, NullabilityInfoContext nullabilityContext)
    {
        var type = property.PropertyType;

        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) is null;
        }

        return nullabilityContext.Create(property).ReadState == NullabilityState.NotNull;
    }
}
