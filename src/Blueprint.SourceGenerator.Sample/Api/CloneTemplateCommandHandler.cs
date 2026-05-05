using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles template clone commands. Creates a new template as a copy of an existing one
/// with the provided new name.
/// </summary>
public sealed class CloneTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Clones the specified template with a new name.
    /// Returns a resource-event result with the template.cloned event.
    /// </summary>
    /// <param name="command">The clone command with source template ID and new name.</param>
    /// <returns>A resource-event result wrapping the cloned template.</returns>
    public ResourceEventResult<TemplateApiResource> Handle(CloneTemplateCommand command)
    {
        var source = store.GetById(command.Id) ?? throw new InvalidOperationException("Template not found");

        var cloned = store.Create(command.NewName!, source.HtmlSource, source.PlainTextSource);

        return new TemplateResourceEvents.Cloned(cloned);
    }
}
