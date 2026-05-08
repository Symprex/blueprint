using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="PatchTemplateSettingsCommand"/> by partially updating a template's
/// display settings. Demonstrates the PATCH HTTP method pattern: only non-null properties
/// are applied, leaving other settings unchanged.
/// </summary>
public class PatchTemplateSettingsCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Applies the partial settings update and signals the updated event.
    /// </summary>
    /// <param name="command">The validated patch-settings command.</param>
    /// <returns>A resource-event result wrapping the updated template.</returns>
    /// <exception cref="NotFoundException">Thrown when no template with the given identifier exists.</exception>
    public ResourceEventResult<TemplateApiResource> Handle(PatchTemplateSettingsCommand command)
    {
        var existing = store.GetById(command.Id)
            ?? throw new NotFoundException($"Template '{command.Id}' not found.");

        return new TemplateResourceEvents.Updated(existing);
    }
}
