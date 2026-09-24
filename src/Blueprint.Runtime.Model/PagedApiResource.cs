using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// A specialisation of <see cref="ListApiResource{T}"/> that represents a list of resources
/// that can be paged on the server.
/// </summary>
/// <remarks>
/// It wraps a page of items with pagination metadata. Use when the consumer needs to navigate through a total result set
/// with page-based offsets (page number + page size). The summary is legacy Blueprint's, which the OpenAPI document
/// describes the resource with.
/// </remarks>
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
    /// The size of the page requested.
    /// </summary>
    /// <remarks>
    /// The maximum number of items per page. The summary is legacy Blueprint's, which the OpenAPI document describes the
    /// property with.
    /// </remarks>
    public int PageSize { get; }

    /// <summary>
    /// The 1-based index of the page requested.
    /// </summary>
    /// <remarks>
    /// The summary is legacy Blueprint's, which the OpenAPI document describes the property with.
    /// </remarks>
    public int CurrentPage { get; }
}
