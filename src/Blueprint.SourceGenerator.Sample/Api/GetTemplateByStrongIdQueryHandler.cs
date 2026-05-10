using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles <see cref="GetTemplateByStrongIdQuery"/> — demonstrates handler consumption of
/// a strongly-typed <see cref="TemplateId"/> route parameter that was parsed at the pipeline
/// level via <see cref="IParsable{TSelf}"/>.
/// </summary>
public class GetTemplateByStrongIdQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Looks up a template by its <see cref="TemplateId"/>, using the GUID value as the store key.
    /// </summary>
    /// <param name="query">The query with the strongly-typed ID.</param>
    /// <returns>The template resource or a not-found result.</returns>
    public Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>> Handle(
        GetTemplateByStrongIdQuery query)
    {
        var template = store.GetById(query.Id.ToString());

        if (template is not null)
        {
            return new OkResult<TemplateApiResource>(template);
        }

        return new NotFoundResult<TemplateApiResource>(
            new TemplateApiResource(query.Id.ToString(), string.Empty, string.Empty, string.Empty));
    }
}
