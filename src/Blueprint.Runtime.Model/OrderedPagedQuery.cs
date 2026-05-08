using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// Base class for query operations that support page-based pagination and optional
/// column ordering. Consumers inherit from this (or the generic variant) to get
/// <see cref="Page"/>, <see cref="PageSize"/>, <see cref="OrderBy"/>, and
/// <see cref="OrderByDescending"/> with built-in <see cref="RangeAttribute"/> validation.
/// </summary>
/// <remarks>
/// This is an opt-in convenience base — operations are not required to extend it.
/// Handlers call <see cref="AsQueryPageDefinition"/> or <see cref="AsQueryOrderDefinition"/>
/// to get strongly-typed pagination/ordering parameters without coupling to this class directly.
/// </remarks>
[PublicAPI]
public abstract class OrderedPagedQuery
{
    /// <summary>
    /// The 1-based index of the page to load.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; set; } = 1;

    /// <summary>
    /// The number of items to load per page.
    /// </summary>
    [Range(1, 500)]
    public int? PageSize { get; set; } = 50;

    /// <summary>
    /// The field to order by, if required.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Whether to order by descending (default: ascending when <see cref="OrderBy"/> is set).
    /// </summary>
    public bool? OrderByDescending { get; set; }

    /// <summary>
    /// Gets the mapping from consumer-facing order-by names to persistence-layer property names.
    /// Override to restrict which columns are orderable and translate camelCase API names to
    /// PascalCase entity properties.
    /// </summary>
    /// <returns>A dictionary mapping allowed order-by names to persistence property names.</returns>
    protected virtual IDictionary<string, string> GetOrderByMappings()
    {
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Creates a <see cref="QueryPageDefinition"/> from the current pagination parameters.
    /// </summary>
    /// <returns>A <see cref="QueryPageDefinition"/> with the resolved page and page size.</returns>
    public QueryPageDefinition AsQueryPageDefinition()
    {
        return new QueryPageDefinition(this.Page!.Value, this.PageSize!.Value);
    }

    /// <summary>
    /// Creates a <see cref="QueryOrderDefinition"/> from the current ordering parameters,
    /// or <c>null</c> if no ordering is specified.
    /// </summary>
    /// <returns>
    /// A <see cref="QueryOrderDefinition"/> when <see cref="OrderBy"/> is set, or <c>null</c>
    /// if no ordering is requested.
    /// </returns>
    /// <exception cref="ValidationException">
    /// Thrown when the specified <see cref="OrderBy"/> value does not appear in the
    /// <see cref="GetOrderByMappings"/> dictionary.
    /// </exception>
    public QueryOrderDefinition? AsQueryOrderDefinition()
    {
        if (this.OrderBy == null)
        {
            return null;
        }

        var orderByMappings = this.GetOrderByMappings();

        if (orderByMappings.Count > 0)
        {
            if (!orderByMappings.TryGetValue(this.OrderBy, out var mappedProperty))
            {
                throw new ValidationException($"Cannot order by '{this.OrderBy}'.");
            }

            return new QueryOrderDefinition(mappedProperty, this.OrderByDescending ?? true);
        }

        return new QueryOrderDefinition(this.OrderBy, this.OrderByDescending ?? true);
    }
}

/// <summary>
/// Generic ordered paged query that returns a <see cref="PagedApiResource{T}"/>.
/// Concrete query classes extend this and implement <see cref="IQuery{TResult}"/>
/// with their specific resource type.
/// </summary>
/// <typeparam name="T">The API resource type contained in each page.</typeparam>
/// <remarks>
/// This base class provides the pagination and ordering properties; the concrete
/// subclass must also implement <see cref="IQuery{TResult}"/> so that the source
/// generator can emit the <c>Configure()</c> method on the concrete type.
/// Use the <c>partial class MyQuery : OrderedPagedQuery, IQuery&lt;PagedApiResource&lt;T&gt;&gt;</c>
/// pattern, or just inherit from this generic variant and add the interface on the concrete class.
/// </remarks>
[PublicAPI]
public abstract class OrderedPagedQuery<T> : OrderedPagedQuery
    where T : ApiResource
{
}
