using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// Encapsulates page-based pagination parameters extracted from a query operation.
/// Handlers can accept this record to compute SKIP/TAKE or OFFSET/LIMIT without
/// coupling directly to the query's property shape.
/// </summary>
/// <param name="Page">The 1-based index of the page to load.</param>
/// <param name="PageSize">The number of items to load per page.</param>
[PublicAPI]
public record QueryPageDefinition(int Page, int PageSize)
{
    /// <summary>
    /// The number of items to skip to reach the start of the requested page.
    /// </summary>
    public int Skip => (this.Page - 1) * this.PageSize;
}
