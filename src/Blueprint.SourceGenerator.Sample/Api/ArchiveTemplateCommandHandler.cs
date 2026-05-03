using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="ArchiveTemplateCommand"/> by removing the template from the shared store.
/// Demonstrates a void handler that produces no response body (HTTP 204).
/// </summary>
public class ArchiveTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Archives (removes) the template identified by the command.
    /// </summary>
    /// <param name="command">The archive-template command with the target identifier.</param>
    public void Handle(ArchiveTemplateCommand command)
    {
        store.Delete(command.Id);
    }
}
