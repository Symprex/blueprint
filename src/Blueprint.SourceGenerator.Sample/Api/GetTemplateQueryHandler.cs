using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="GetTemplateQuery"/> by looking up a single template from the shared store.
/// Returns <see cref="OkResult{T}"/> when the template exists, or <see cref="NotFoundResult{T}"/>
/// when the identifier is unknown — demonstrating how a handler produces discriminated union results
/// that the generated pipeline maps to distinct HTTP status codes.
/// </summary>
public class GetTemplateQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Retrieves a template by its identifier, or signals not-found when the identifier is unknown.
    /// </summary>
    /// <param name="query">The get-template query containing the identifier.</param>
    /// <returns>An <see cref="OkResult{T}"/> wrapping the template, or a <see cref="NotFoundResult{T}"/> with a minimal error payload.</returns>
    public Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>> Handle(GetTemplateQuery query)
    {
        var template = store.GetById(query.Id);

        if (template is not null)
        {
            return new OkResult<TemplateApiResource>(template);
        }

        return new NotFoundResult<TemplateApiResource>(
            new TemplateApiResource(query.Id, string.Empty, string.Empty, string.Empty));
    }
}
