using Blueprint.Model;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="GetPagedTemplatesQuery"/> by returning a paged slice of
/// the template store, demonstrating the <see cref="PagedApiResource{T}"/> pattern.
/// </summary>
public class GetPagedTemplatesQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Returns a paged result of templates from the store.
    /// </summary>
    /// <param name="query">The paged query with pagination parameters.</param>
    /// <returns>A paged resource containing the requested page of templates.</returns>
    public PagedApiResource<TemplateApiResource> Handle(GetPagedTemplatesQuery query)
    {
        var all = store.GetAll();
        var page = all
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();

        return new PagedApiResource<TemplateApiResource>(page, all.Length, query.PageSize, query.Page);
    }
}
