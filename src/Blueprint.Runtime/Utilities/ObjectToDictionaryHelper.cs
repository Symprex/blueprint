using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Utilities;

/// <summary>
/// Extension methods that convert an object's public properties into dictionary representations.
/// Used primarily for template model serialisation (e.g. email templates) where the model needs
/// to be decoupled from its concrete type.
/// </summary>
public static class ObjectToDictionaryHelper
{
    /// <summary>
    /// Converts the given object into a <see cref="Dictionary{TKey,TValue}"/> of all
    /// public property values, including <see langword="null"/> values.
    /// </summary>
    /// <param name="source">The object to convert.</param>
    /// <returns>A dictionary representation of <paramref name="source"/>.</returns>
    [RequiresUnreferencedCode("Uses TypeDescriptor.GetProperties which requires unreferenced code.")]
    public static Dictionary<string, object> ToDictionary(this object source)
    {
        return source.ToDictionary<object>();
    }

    /// <summary>
    /// Converts the given object into a <see cref="Dictionary{TKey,TValue}"/> by calling
    /// <see cref="object.ToString"/> on each public property value.
    /// </summary>
    /// <param name="source">The object to convert.</param>
    /// <returns>A string-valued dictionary representation of <paramref name="source"/>.</returns>
    [RequiresUnreferencedCode("Uses TypeDescriptor.GetProperties which requires unreferenced code.")]
    public static Dictionary<string, string> ToStringDictionary(this object source)
    {
        return ToDictionary(source, static (p, _) => true, static v => v?.ToString()!);
    }

    /// <summary>
    /// Converts the given object into a <see cref="Dictionary{TKey,TValue}"/> including only
    /// properties whose type is assignable to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The value type to extract.</typeparam>
    /// <param name="source">The object to convert.</param>
    /// <returns>A dictionary of matching property values.</returns>
    [RequiresUnreferencedCode("Uses TypeDescriptor.GetProperties which requires unreferenced code.")]
    public static Dictionary<string, T> ToDictionary<T>(this object source)
    {
        return ToDictionary(source, static (p, _) => typeof(T).IsAssignableFrom(p.PropertyType), static v => (T)v);
    }

    /// <summary>
    /// Converts the given object into a dictionary using a custom filter and value projection.
    /// </summary>
    /// <typeparam name="T">The projected value type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="filter">A predicate controlling which properties are included.</param>
    /// <param name="toValue">A projection from the raw property value to <typeparamref name="T"/>.</param>
    /// <returns>A dictionary of filtered, projected property values.</returns>
    [RequiresUnreferencedCode("Uses TypeDescriptor.GetProperties which requires unreferenced code.")]
    public static Dictionary<string, T> ToDictionary<T>(
        this object source,
        Func<PropertyDescriptor, object, bool> filter,
        Func<object, T> toValue)
    {
        if (source == null)
        {
            return new Dictionary<string, T>();
        }

        var dictionary = new Dictionary<string, T>();

        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(source))
        {
            var value = property.GetValue(source);

            if (filter(property, value!))
            {
                var key = property.Name;

                if (property.Attributes[typeof(DictionaryKeyAttribute)] is DictionaryKeyAttribute dictionaryKey)
                {
                    key = dictionaryKey.Name;
                }

                dictionary.Add(key, toValue(value!));
            }
        }

        return dictionary;
    }
}
