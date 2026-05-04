using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="CreateTemplateCommand"/> by persisting a new template in the shared store
/// and returning a resource-event result that the HTTP host translates to 201 Created with a JSON envelope.
/// </summary>
public class CreateTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Creates a new template from the validated command properties and returns a resource-event
    /// indicating the template was created.
    /// </summary>
    /// <param name="command">The validated create-template command.</param>
    /// <returns>A resource-event result wrapping the created template.</returns>
    public ResourceEventResult<TemplateApiResource> Handle(CreateTemplateCommand command)
    {
        var template = store.Create(
            command.Name!,
            command.HtmlSource ?? string.Empty,
            command.PlainTextSource ?? string.Empty);

        return new TemplateResourceEvents.Created(template);
    }
}
