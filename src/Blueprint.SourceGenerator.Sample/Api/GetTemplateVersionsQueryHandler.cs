using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles template version history queries. Returns a synthetic version list
/// since the in-memory store does not track real versions.
/// </summary>
public sealed class GetTemplateVersionsQueryHandler
{
    private readonly TemplateStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTemplateVersionsQueryHandler"/> class.
    /// </summary>
    /// <param name="store">The in-memory template store.</param>
    public GetTemplateVersionsQueryHandler(TemplateStore store)
    {
        this._store = store;
    }

    /// <summary>
    /// Returns the version history for the specified template. Returns a synthetic single-version
    /// entry since the in-memory store tracks only the current state.
    /// </summary>
    /// <param name="operation">The versions query with the template identifier.</param>
    /// <returns>An array of version resources, or empty if the template does not exist.</returns>
    public Task<TemplateVersionApiResource[]> HandleAsync(GetTemplateVersionsQuery operation)
    {
        var template = this._store.GetById(operation.Id);
        if (template is null)
        {
            return Task.FromResult(Array.Empty<TemplateVersionApiResource>());
        }

        var version = new TemplateVersionApiResource(1, DateTimeOffset.UtcNow, operation.Id);
        return Task.FromResult(new[] { version });
    }
}
