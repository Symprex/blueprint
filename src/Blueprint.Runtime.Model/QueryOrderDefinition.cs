using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// Encapsulates query ordering parameters — a property name and direction.
/// Handlers accept this to build ORDER BY clauses without coupling to the
/// query's property shape.
/// </summary>
[PublicAPI]
public sealed class QueryOrderDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryOrderDefinition"/> class.
    /// </summary>
    /// <param name="orderBy">The property name to order by.</param>
    /// <param name="descending">Whether to order descending (default: <c>true</c>).</param>
    public QueryOrderDefinition(string orderBy, bool descending = true)
    {
        this.OrderBy = orderBy;
        this.Descending = descending;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryOrderDefinition"/> class
    /// with a nullable descending flag (legacy S365 compatibility overload).
    /// </summary>
    /// <param name="orderBy">The property name to order by.</param>
    /// <param name="descending">Whether to order descending; <c>null</c> defaults to <c>true</c>.</param>
    public QueryOrderDefinition(string orderBy, bool? descending)
    {
        this.OrderBy = orderBy;
        this.Descending = descending ?? true;
    }

    /// <summary>
    /// The property name to order by.
    /// </summary>
    public string OrderBy { get; }

    /// <summary>
    /// Whether to order descending.
    /// </summary>
    public bool Descending { get; }
}
