using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// A paged list resource that wraps a page of items with pagination metadata.
/// Use when the consumer needs to navigate through a total result set with
/// page-based offsets (page number + page size).
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
[PublicAPI]
public class PagedApiResource<T> : ListApiResource<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagedApiResource{T}"/> class.
    /// </summary>
    /// <param name="values">The items on the current page.</param>
    /// <param name="total">The total number of items across all pages.</param>
    /// <param name="pageSize">The maximum number of items per page.</param>
    /// <param name="currentPage">The 1-based current page number.</param>
    public PagedApiResource(IEnumerable<T> values, long total, int pageSize, int currentPage)
        : base(values)
    {
        this.Total = total;
        this.PageSize = pageSize;
        this.CurrentPage = currentPage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PagedApiResource{T}"/> class
    /// for JSON deserialization.
    /// </summary>
    [JsonConstructor]
    public PagedApiResource()
    {
    }

    /// <summary>
    /// The maximum number of items per page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// The 1-based index of the current page.
    /// </summary>
    public int CurrentPage { get; }
}
