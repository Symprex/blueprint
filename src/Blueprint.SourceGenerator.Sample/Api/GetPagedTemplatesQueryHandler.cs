using Blueprint.Model;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="GetPagedTemplatesQuery"/> by returning a paged slice of
/// the template store, demonstrating the <see cref="OrderedPagedQuery{T}"/> and
/// <see cref="QueryPageDefinition"/> patterns.
/// </summary>
public class GetPagedTemplatesQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Returns a paged result of templates from the store, using the query's
    /// <see cref="OrderedPagedQuery.AsQueryPageDefinition"/> for pagination parameters.
    /// </summary>
    /// <param name="query">The paged query with pagination and optional ordering parameters.</param>
    /// <returns>A paged resource containing the requested page of templates.</returns>
    public PagedApiResource<TemplateApiResource> Handle(GetPagedTemplatesQuery query)
    {
        var page = query.AsQueryPageDefinition();
        var all = store.GetAll();

        if (query.NameFilter is { Length: > 0 } filter)
        {
            all = all.Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        var items = all
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToArray();

        return new PagedApiResource<TemplateApiResource>(items, all.Length, page.PageSize, page.Page);
    }
}
