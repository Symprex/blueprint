using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="CreateTemplateCommand"/> by persisting a new template in the shared store
/// and returning a resource-event result that the HTTP host translates to 201 Created with a JSON envelope.
/// Also demonstrates the unit-of-work persistence seam: changes tracked in the handler are committed
/// automatically by the generated pipeline's PostExecution stage.
/// </summary>
public class CreateTemplateCommandHandler(TemplateStore store, SampleMartenUnitOfWork unitOfWork)
{
    /// <summary>
    /// Creates a new template from the validated command properties and returns a resource-event
    /// indicating the template was created. Tracks the creation as a pending change in the unit-of-work
    /// so the generated pipeline commits it alongside any other tracked changes.
    /// </summary>
    /// <param name="command">The validated create-template command.</param>
    /// <returns>A resource-event result wrapping the created template.</returns>
    public ResourceEventResult<TemplateApiResource> Handle(CreateTemplateCommand command)
    {
        var template = store.Create(
            command.Name!,
            command.HtmlSource ?? string.Empty,
            command.PlainTextSource ?? string.Empty);

        unitOfWork.Store("Template", template.Id);

        return new TemplateResourceEvents.Created(template);
    }
}
