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

    /// <summary>
    /// Returns whether the elements of a collection of type <paramref name="collectionType"/> are <see cref="Nullable{T}"/>, as
    /// <c>double?[]</c> and <c>List&lt;int?&gt;</c> declare them. Unlike <see cref="AreItemsNullable"/>, which reads the
    /// annotation on a reference element, a nullable value type is part of the element's own type.
    /// </summary>
    /// <param name="collectionType">The type of the collection, which a property or a serialised field has, or <see langword="null"/> when it is not known.</param>
    /// <returns><see langword="true"/> when the collection's element type is a nullable value type.</returns>
    public static bool AreValueItemsNullable(Type? collectionType)
    {
        // The element the projections render items for: the T of the type's IEnumerable<T>, however the type is declared.
        var element = OpenApiOperationRenderingContext.GetCollectionElementType(collectionType, dictionaryValues: false);

        return element is not null && Nullable.GetUnderlyingType(element) is not null;
    }
}
