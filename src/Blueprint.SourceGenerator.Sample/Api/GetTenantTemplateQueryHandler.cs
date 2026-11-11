using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="GetTenantTemplateQuery"/> by looking the template up in the shared store. Tenant isolation is
/// checked before this runs, by <see cref="TenantIsolationApiAuthoriser"/>.
/// </summary>
public class GetTenantTemplateQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Retrieves the template.
    /// </summary>
    /// <param name="query">The tenant-scoped query.</param>
    /// <returns>The template.</returns>
    /// <exception cref="NotFoundException">No template with the given identifier exists.</exception>
    public TemplateApiResource Handle(GetTenantTemplateQuery query)
    {
        return store.GetById(query.Id)
            ?? throw new NotFoundException($"Template '{query.Id}' not found for tenant '{query.TenantId}'.");
    }
}
