using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles template publish commands. Marks the template as published (in a real app this would
/// transition state; in the in-memory sample it simply returns the current template).
/// </summary>
public sealed class PublishTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Publishes the specified template. Returns a resource-event result with the template.published event.
    /// </summary>
    /// <param name="command">The publish command with the template identifier.</param>
    /// <returns>A resource-event result wrapping the published template.</returns>
    public ResourceEventResult<TemplateApiResource> Handle(PublishTemplateCommand command)
    {
        var template = store.GetById(command.Id) ?? throw new InvalidOperationException("Template not found");

        return new TemplateResourceEvents.Published(template);
    }
}
