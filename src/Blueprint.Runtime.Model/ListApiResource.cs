using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// A generic list resource that wraps a collection of items with standard API resource
/// metadata (<c>$object</c>, <c>$links</c>). The <c>$object</c> value is automatically
/// derived as <c>list.{typeName}</c> from the item type.
/// </summary>
/// <typeparam name="T">The type of items in the list.</typeparam>
[PublicAPI]
public class ListApiResource<T> : ApiResource, IApiResourceCollection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListApiResource{T}"/> class
    /// with the given items.
    /// </summary>
    /// <param name="values">The items to include in this list resource.</param>
    public ListApiResource(IEnumerable<T> values)
    {
        var asList = values as IReadOnlyList<T> ?? values.ToList();

        this.Values = asList;
        this.Total = asList.Count;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ListApiResource{T}"/> class
    /// for JSON deserialization.
    /// </summary>
    [JsonConstructor]
    public ListApiResource()
    {
        this.Values = [];
    }

    /// <inheritdoc />
    [JsonPropertyName("$object")]
    public override string Object => $"list.{GetItemTypeName()}";

    /// <summary>
    /// The values of this list.
    /// </summary>
    /// <remarks>
    /// The summary is legacy Blueprint's, which the OpenAPI document describes the property with.
    /// </remarks>
    public IEnumerable<T> Values { get; }

    /// <summary>
    /// The total count of values in this list.
    /// </summary>
    /// <remarks>
    /// For <see cref="ListApiResource{T}"/>, this equals the count of <see cref="Values"/>. For
    /// <see cref="PagedApiResource{T}"/>, this represents the total count across all pages. The summary is legacy
    /// Blueprint's, which the OpenAPI document describes the property with.
    /// </remarks>
    public long Total { get; protected internal set; }

    /// <inheritdoc />
    public IEnumerable<object> GetEnumerable()
    {
        return this.Values.Cast<object>();
    }

    /// <summary>
    /// Gets the friendly name for the item type, used in the <c>$object</c> property.
    /// Uses camelCase of the type name, stripping common suffixes like "ApiResource".
    /// </summary>
    private static string GetItemTypeName()
    {
        var name = typeof(T).Name;

        // Strip common resource suffixes for cleaner $object values
        if (name.EndsWith("ApiResource", StringComparison.Ordinal))
        {
            name = name[..^"ApiResource".Length];
        }
        else if (name.EndsWith("Resource", StringComparison.Ordinal))
        {
            name = name[..^"Resource".Length];
        }

        // camelCase the result
        return name.Length > 0
            ? char.ToLowerInvariant(name[0]) + name[1..]
            : name;
    }
}
