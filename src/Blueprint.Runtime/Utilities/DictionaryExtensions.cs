namespace Blueprint.Utilities;

/// <summary>
/// Extension methods for <see cref="IDictionary{TKey, TValue}"/>.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    /// Gets an existing value from the dictionary, or adds a new one using the specified
    /// factory function if the key does not exist.
    /// </summary>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <param name="dictionary">The dictionary to search.</param>
    /// <param name="key">The key to look up or add.</param>
    /// <param name="valueCreator">A factory function to create the value when the key is absent.</param>
    /// <returns>The existing or newly created value.</returns>
    public static TValue GetOrAdd<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TKey, TValue> valueCreator)
    {
        if (dictionary.TryGetValue(key, out var value))
        {
            return value;
        }

        return dictionary[key] = valueCreator(key);
    }
}
