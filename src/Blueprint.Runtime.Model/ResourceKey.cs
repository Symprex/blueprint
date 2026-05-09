using System.ComponentModel;

namespace Blueprint;

/// <summary>
/// Represents a resource key in the format <c>[Type]/[Id]</c> where the Id portion
/// is parsed via a <see cref="TypeConverter"/> for maximum flexibility (supports
/// <see cref="Guid"/>, <see cref="int"/>, <see cref="long"/>, etc.).
/// </summary>
/// <typeparam name="T">The type of the resource identifier.</typeparam>
public sealed class ResourceKey<T>
{
    private ResourceKey(string resourceType, T id)
    {
        this.ResourceType = resourceType;
        this.Id = id;
    }

    /// <summary>Gets the identifier portion of this resource key.</summary>
    public T Id { get; }

    /// <summary>Gets the resource type portion of this resource key.</summary>
    public string ResourceType { get; }

    /// <summary>
    /// Parses a resource key string in the format <c>[Type]/[Id]</c>.
    /// </summary>
    /// <param name="resourceKey">The string to parse.</param>
    /// <returns>The parsed <see cref="ResourceKey{T}"/>.</returns>
    /// <exception cref="FormatException">When the string is not in a valid resource key format.</exception>
    public static ResourceKey<T> Parse(string resourceKey)
    {
        if (TryParse(resourceKey, out var parsedResourceKey))
        {
            return parsedResourceKey!;
        }

        throw new FormatException(
            $"Resource key '{resourceKey}' is in an invalid format. A resource key must be in the format '[Type]/[Id]' where Id is a {typeof(T).Name}.");
    }

    /// <summary>
    /// Extracts just the id from a resource key string.
    /// </summary>
    /// <param name="resourceKey">The string to parse.</param>
    /// <returns>The Id portion of the resource key.</returns>
    public static T ParseId(string resourceKey)
    {
        return Parse(resourceKey).Id;
    }

    /// <summary>
    /// Attempts to parse a resource key string without throwing.
    /// </summary>
    /// <param name="resourceKey">The string to parse.</param>
    /// <param name="parsedResourceKey">The parsed result, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? resourceKey, out ResourceKey<T>? parsedResourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey))
        {
            parsedResourceKey = null;
            return true;
        }

        var separatorIndex = resourceKey.IndexOf('/');

        if (separatorIndex <= 0 || separatorIndex == resourceKey.Length - 1)
        {
            parsedResourceKey = null;
            return false;
        }

        // Ensure there's only one separator
        if (resourceKey.IndexOf('/', separatorIndex + 1) >= 0)
        {
            parsedResourceKey = null;
            return false;
        }

        var type = resourceKey[..separatorIndex];
        var idPart = resourceKey[(separatorIndex + 1)..];

        try
        {
            var converter = TypeDescriptor.GetConverter(typeof(T));
            var id = (T)converter.ConvertFromInvariantString(idPart)!;
            parsedResourceKey = new ResourceKey<T>(type, id);
            return true;
        }
        catch (Exception)
        {
            parsedResourceKey = null;
            return false;
        }
    }

    /// <summary>
    /// Returns the string representation in the format <c>[Type]/[Id]</c>.
    /// </summary>
    /// <returns>The resource key as a string.</returns>
    public override string ToString()
    {
        return this.ResourceType + "/" + this.Id;
    }
}
