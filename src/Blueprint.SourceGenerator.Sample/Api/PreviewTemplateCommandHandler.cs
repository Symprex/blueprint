using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles template preview commands by performing simple placeholder replacement
/// on the template content with the provided merge field values.
/// </summary>
public sealed class PreviewTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Renders the template by replacing <c>{{FieldName}}</c> placeholders with the
    /// corresponding merge field values.
    /// </summary>
    /// <param name="command">The preview command with template ID and merge fields.</param>
    /// <returns>A preview resource with the rendered content.</returns>
    public TemplatePreviewApiResource Handle(PreviewTemplateCommand command)
    {
        var template = store.GetById(command.Id) ?? throw new InvalidOperationException("Template not found");

        // Keyed as placeholders are matched, ignoring case, so a recipient value replaces a merge field of the same name.
        var mergeFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in command.MergeFields)
        {
            mergeFields[key] = value;
        }

        if (command.Recipient is { } recipient)
        {
            mergeFields["Recipient.Name"] = recipient.Name ?? string.Empty;
            mergeFields["Recipient.Email"] = recipient.Email ?? string.Empty;
        }

        var html = ApplyMergeFields(template.HtmlSource, mergeFields);
        var plainText = ApplyMergeFields(template.PlainTextSource, mergeFields);

        return new TemplatePreviewApiResource(command.Id, html, plainText);
    }

    private static string ApplyMergeFields(string source, Dictionary<string, string> mergeFields)
    {
        foreach (var (key, value) in mergeFields)
        {
            source = source.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
        }

        return source;
    }
}
