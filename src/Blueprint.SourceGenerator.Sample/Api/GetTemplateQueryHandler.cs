using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="GetTemplateQuery"/> by looking up a single template from the shared store.
/// Returns the template directly; returns a deterministic fallback when not found to keep the
/// sample app operational without error-path complexity at this layer.
/// </summary>
public class GetTemplateQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Retrieves a template by its identifier, or a placeholder when the identifier is unknown.
    /// </summary>
    /// <param name="query">The get-template query containing the identifier.</param>
    /// <returns>The matching template resource, or a placeholder when not found.</returns>
    public TemplateApiResource Handle(GetTemplateQuery query)
    {
        return store.GetById(query.Id)
            ?? new TemplateApiResource(query.Id, $"Template {query.Id}", string.Empty, string.Empty);
    }
}
