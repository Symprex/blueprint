using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="GetTemplateHistoryQuery"/> by returning version entries from the store,
/// optionally filtered by the validated date range.
/// </summary>
public class GetTemplateHistoryQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Retrieves template version history. Cross-property validation (StartDate &lt; EndDate,
    /// conditional RequiredIf, mutual exclusivity via NotAllowedIf) is enforced by the generated
    /// pipeline before this handler is reached.
    /// </summary>
    /// <param name="query">The validated history query.</param>
    /// <returns>An array of template version resources.</returns>
    public TemplateVersionApiResource[] Handle(GetTemplateHistoryQuery query)
    {
        var template = store.GetById(query.Id);
        if (template is null)
        {
            return [];
        }

        // In production, filter by date range. Here we return the single known version.
        return
        [
            new TemplateVersionApiResource(1, DateTimeOffset.UtcNow, query.Id),
        ];
    }
}
