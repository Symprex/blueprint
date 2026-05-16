namespace Blueprint.Utilities;

/// <summary>
/// Overrides the dictionary key name used by <see cref="ObjectToDictionaryHelper.ToDictionary"/>
/// when converting an object's properties to a <see cref="Dictionary{TKey,TValue}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DictionaryKeyAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="DictionaryKeyAttribute"/> class.
    /// </summary>
    /// <param name="name">The key name to use in the resulting dictionary.</param>
    public DictionaryKeyAttribute(string name)
    {
        this.Name = name;
    }

    /// <summary>
    /// Gets the key name to use in the resulting dictionary.
    /// </summary>
    public string Name { get; }
}
