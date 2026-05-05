using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles template search queries with case-insensitive name matching and pagination.
/// </summary>
public sealed class SearchTemplatesQueryHandler
{
    private readonly TemplateStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchTemplatesQueryHandler"/> class.
    /// </summary>
    /// <param name="store">The in-memory template store.</param>
    public SearchTemplatesQueryHandler(TemplateStore store)
    {
        this._store = store;
    }

    /// <summary>
    /// Searches templates whose name contains the query term (case-insensitive).
    /// </summary>
    /// <param name="operation">The search operation with query parameters.</param>
    /// <returns>A paginated array of matching template resources.</returns>
    public Task<TemplateApiResource[]> HandleAsync(SearchTemplatesQuery operation)
    {
        var results = this._store.GetAll()
            .Where(t => t.Name.Contains(operation.Q!, StringComparison.OrdinalIgnoreCase))
            .Skip((operation.Page - 1) * operation.PageSize)
            .Take(operation.PageSize)
            .ToArray();

        return Task.FromResult(results);
    }
}
