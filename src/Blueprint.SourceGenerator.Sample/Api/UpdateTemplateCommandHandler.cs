using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="UpdateTemplateCommand"/> by updating an existing template in the shared store
/// and returning a body-bearing resource-event result (template.updated). Throws <see cref="NotFoundException"/>
/// when the template identifier is unknown — demonstrating the typed exception escape hatch pattern
/// alongside the discriminated union result pattern shown in <see cref="GetTemplateQueryHandler"/>.
/// </summary>
public class UpdateTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Updates the template matching the command's identifier and signals the updated event.
    /// </summary>
    /// <param name="command">The validated update-template command.</param>
    /// <returns>A resource-event result wrapping the updated template.</returns>
    /// <exception cref="NotFoundException">Thrown when no template with the given identifier exists.</exception>
    public ResourceEventResult<TemplateApiResource> Handle(UpdateTemplateCommand command)
    {
        var updated = store.Update(command.Id, command.Name!, command.HtmlSource ?? string.Empty, command.PlainTextSource ?? string.Empty)
            ?? throw new NotFoundException($"Template '{command.Id}' not found.");

        return new TemplateResourceEvents.Updated(updated);
    }
}
