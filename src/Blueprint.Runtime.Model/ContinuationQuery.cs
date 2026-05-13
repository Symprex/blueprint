using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// Base class for query operations that use cursor-based (continuation token) pagination.
/// Consumers inherit from this (or the generic variant) to get <see cref="ContinuationToken"/>
/// and <see cref="PageSize"/> with built-in <see cref="RangeAttribute"/> validation.
/// </summary>
/// <remarks>
/// Use this when total-count pagination is expensive (e.g. event stores, cursor-based APIs,
/// CosmosDB). For offset/page-based pagination, use <see cref="OrderedPagedQuery"/> instead.
/// </remarks>
[PublicAPI]
public abstract class ContinuationQuery
{
    /// <summary>
    /// An opaque token from the previous response that identifies the position to continue
    /// reading from. <c>null</c> on the first request.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// The number of items to load per page.
    /// </summary>
    [Range(1, 500)]
    public int? PageSize { get; set; } = 50;
}

/// <summary>
/// Generic continuation query that returns a <see cref="ContinuationPagedApiResource{T}"/>.
/// Concrete query classes extend this and implement <see cref="IQuery{TResult}"/>
/// with their specific resource type.
/// </summary>
/// <typeparam name="T">The API resource type contained in each page.</typeparam>
/// <remarks>
/// This base class provides the continuation token and page-size properties; the concrete
/// subclass must also implement <see cref="IQuery{TResult}"/> so that the source
/// generator can emit the <c>Configure()</c> method on the concrete type.
/// Use the <c>partial class MyQuery : ContinuationQuery&lt;T&gt;, IQuery&lt;ContinuationPagedApiResource&lt;T&gt;&gt;</c>
/// pattern.
/// </remarks>
[PublicAPI]
public abstract class ContinuationQuery<T> : ContinuationQuery
    where T : ApiResource
{
}
