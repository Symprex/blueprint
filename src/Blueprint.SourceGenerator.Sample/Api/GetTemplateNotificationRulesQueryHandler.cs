using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles notification rule queries. Returns a synthetic rule since the in-memory store does not
/// track notification configuration.
/// </summary>
public sealed class GetTemplateNotificationRulesQueryHandler
{
    private readonly TemplateStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTemplateNotificationRulesQueryHandler"/> class.
    /// </summary>
    /// <param name="store">The in-memory template store.</param>
    public GetTemplateNotificationRulesQueryHandler(TemplateStore store)
    {
        this._store = store;
    }

    /// <summary>
    /// Returns the notification rules for the specified template, or an empty array when the template
    /// does not exist.
    /// </summary>
    /// <param name="operation">The notification rules query with the template identifier.</param>
    /// <returns>An array of notification rules.</returns>
    public Task<TemplateNotificationRuleApiResource[]> HandleAsync(GetTemplateNotificationRulesQuery operation)
    {
        var template = this._store.GetById(operation.Id);
        if (template is null)
        {
            return Task.FromResult(Array.Empty<TemplateNotificationRuleApiResource>());
        }

        var rule = new TemplateNotificationRuleApiResource
        {
            Enabled = true,
            RecipientEmail = "owner@example.com",
            Subject = "Template updated",
        };

        return Task.FromResult(new[] { rule });
    }
}
