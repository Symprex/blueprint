using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Describes a dictionary's values as legacy Blueprint's NJsonSchema 11.1 documented them in <c>additionalProperties</c>:
/// a read-only dictionary as a dictionary, an open value as any value, and a value's nullability from its annotation.
/// </summary>
internal static class OpenApiDictionaryValues
{
    /// <summary>
    /// Returns the value type of <paramref name="type"/> when it is, or implements, <see cref="IDictionary{TKey,TValue}"/>
    /// or <see cref="IReadOnlyDictionary{TKey,TValue}"/>; otherwise <see langword="null"/>.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The dictionary's value type, or <see langword="null"/> when the type is not a dictionary.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved by the serializer context for types it covers.")]
    public static Type? GetValueType(Type type)
    {
        var dictionary = FindDictionaryInterface(type, typeof(IDictionary<,>)) ?? FindDictionaryInterface(type, typeof(IReadOnlyDictionary<,>));

        return dictionary?.GetGenericArguments()[1];
    }

    /// <summary>
    /// Returns whether a dictionary's values of <paramref name="valueType"/> are any value, which legacy documented as
    /// <c>{}</c>: an <see cref="object"/>, or a <see cref="JsonElement"/>, whatever their nullability.
    /// </summary>
    /// <param name="valueType">The dictionary's value type.</param>
    /// <returns><see langword="true"/> when the values are documented as any value.</returns>
    public static bool IsAnyValue(Type valueType)
    {
        var underlying = Nullable.GetUnderlyingType(valueType) ?? valueType;

        return underlying == typeof(object) || underlying == typeof(JsonElement);
    }

    /// <summary>
    /// Returns whether the values of the dictionary <paramref name="property"/> holds may be null, as legacy took it from
    /// the value's annotation: a <see cref="Nullable{T}"/> value, or a reference type annotated as nullable. A reference
    /// type with no annotation, or a dictionary type whose value is not its own last type argument, is not.
    /// </summary>
    /// <param name="property">The CLR property holding the dictionary, if there is one to read annotations from.</param>
    /// <param name="valueType">The dictionary's value type, from <see cref="GetValueType"/>.</param>
    /// <param name="nullabilityContext">The context that reads the property's nullable annotations.</param>
    /// <returns><see langword="true"/> when the dictionary's values are documented as nullable.</returns>
    public static bool AreValuesNullable(PropertyInfo? property, Type valueType, NullabilityInfoContext nullabilityContext)
    {
        if (valueType.IsValueType)
        {
            return Nullable.GetUnderlyingType(valueType) is not null;
        }

        // A reference type's annotation is read from the property's own type arguments, where the value is the last.
        if (property is null)
        {
            return false;
        }

        var propertyType = property.PropertyType;

        if (!propertyType.IsGenericType || propertyType.GetGenericArguments()[^1] != valueType)
        {
            return false;
        }

        var typeArguments = nullabilityContext.Create(property).GenericTypeArguments;

        return typeArguments.Length > 0 && typeArguments[^1].ReadState == NullabilityState.Nullable;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved by the serializer context for types it covers.")]
    private static Type? FindDictionaryInterface(Type type, Type definition)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
        {
            return type;
        }

        return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == definition);
    }
}
