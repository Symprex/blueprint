using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="CreateTemplateCommand"/> by persisting a new template in the shared store.
/// </summary>
public class CreateTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Creates a new template from the validated command properties.
    /// </summary>
    /// <param name="command">The validated create-template command.</param>
    /// <returns>The newly created template resource.</returns>
    public TemplateApiResource Handle(CreateTemplateCommand command)
    {
        return store.Create(
            command.Name!,
            command.HtmlSource ?? string.Empty,
            command.PlainTextSource ?? string.Empty);
    }
}
