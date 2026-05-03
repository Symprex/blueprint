using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="GetTemplatesQuery"/> by returning all templates from the shared store.
/// </summary>
public class GetTemplatesQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Returns all templates currently in the store.
    /// </summary>
    /// <param name="query">The list templates query.</param>
    /// <returns>An array of all template resources.</returns>
    public TemplateApiResource[] Handle(GetTemplatesQuery query)
    {
        return store.GetAll();
    }
}
