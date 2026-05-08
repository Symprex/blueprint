using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// A continuation-style paged resource that uses a <see cref="HasMore"/> flag instead of
/// a total count. Suitable for data sources where counting all results is expensive
/// (e.g. event stores, cursor-based APIs).
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
[PublicAPI]
public class ContinuationPagedApiResource<T> : ApiResource, IApiResourceCollection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContinuationPagedApiResource{T}"/> class.
    /// </summary>
    /// <param name="values">The items on the current page.</param>
    /// <param name="hasMore">Whether more items exist beyond this page.</param>
    /// <param name="pageSize">The maximum number of items per page.</param>
    /// <param name="currentPage">The 1-based current page number.</param>
    public ContinuationPagedApiResource(IEnumerable<T> values, bool hasMore, int pageSize, int currentPage)
    {
        var asList = values as IReadOnlyList<T> ?? values.ToList();

        this.Values = asList;
        this.HasMore = hasMore;
        this.PageSize = pageSize;
        this.CurrentPage = currentPage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContinuationPagedApiResource{T}"/> class
    /// for JSON deserialization.
    /// </summary>
    [JsonConstructor]
    public ContinuationPagedApiResource()
    {
        this.Values = [];
    }

    /// <inheritdoc />
    [JsonPropertyName("$object")]
    public override string Object => $"list.{GetItemTypeName()}";

    /// <summary>
    /// The items on the current page.
    /// </summary>
    public IEnumerable<T> Values { get; }

    /// <summary>
    /// Whether more items exist beyond the current page.
    /// </summary>
    public bool HasMore { get; }

    /// <summary>
    /// The maximum number of items per page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// The 1-based index of the current page.
    /// </summary>
    public int CurrentPage { get; }

    /// <inheritdoc />
    public IEnumerable<object> GetEnumerable()
    {
        return this.Values.Cast<object>();
    }

    private static string GetItemTypeName()
    {
        var name = typeof(T).Name;

        if (name.EndsWith("ApiResource", StringComparison.Ordinal))
        {
            name = name[..^"ApiResource".Length];
        }
        else if (name.EndsWith("Resource", StringComparison.Ordinal))
        {
            name = name[..^"Resource".Length];
        }

        return name.Length > 0
            ? char.ToLowerInvariant(name[0]) + name[1..]
            : name;
    }
}
