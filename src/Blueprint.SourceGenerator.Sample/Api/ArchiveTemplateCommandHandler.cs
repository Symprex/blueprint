using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="ArchiveTemplateCommand"/> by removing the template from the shared store
/// and returning a key-only resource-event result (HTTP 201 with no body).
/// </summary>
public class ArchiveTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Archives (removes) the template identified by the command and signals the deletion event.
    /// </summary>
    /// <param name="command">The archive-template command with the target identifier.</param>
    /// <returns>A key-only resource-event result carrying identity without a response body.</returns>
    public KeyOnlyResourceEventResult Handle(ArchiveTemplateCommand command)
    {
        store.Delete(command.Id);

        return new TemplateResourceEvents.Archived(command.Id);
    }
}
