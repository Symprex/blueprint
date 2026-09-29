using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// Returns whether the elements of the collection <paramref name="property"/> are annotated as nullable, as
    /// <c>IEnumerable&lt;Block?&gt;</c> annotates them, which legacy Blueprint's NJsonSchema documented as nullable items.
    /// </summary>
    /// <param name="property">The CLR property, or <see langword="null"/> when it is not known.</param>
    /// <param name="nullabilityContext">The context that reads the property's nullable annotations.</param>
    /// <returns><see langword="true"/> when the property's element type is a reference type annotated as nullable.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The generic type definition of a covered closed generic body type is preserved alongside it.")]
    public static bool AreItemsNullable(PropertyInfo? property, NullabilityInfoContext nullabilityContext)
    {
        if (property is null)
        {
            return false;
        }

        // An element declared as a generic type parameter (ListApiResource<T>.Values) has no annotation of its own, which
        // reflection reports as nullable for any unconstrained parameter; legacy documented such items as they were.
        if (property.DeclaringType is { IsGenericType: true } declaringType
            && declaringType.GetGenericTypeDefinition().GetProperty(property.Name) is { } definition
            && (definition.PropertyType.IsArray
                ? definition.PropertyType.GetElementType()
                : definition.PropertyType.GetGenericArguments() is [var argument] ? argument : null) is { IsGenericParameter: true })
        {
            return false;
        }

        var nullability = nullabilityContext.Create(property);
        var element = nullability.ElementType
            ?? (nullability.GenericTypeArguments.Length == 1 ? nullability.GenericTypeArguments[0] : null);

        return element is { Type.IsValueType: false, ReadState: NullabilityState.Nullable };
    }
}
