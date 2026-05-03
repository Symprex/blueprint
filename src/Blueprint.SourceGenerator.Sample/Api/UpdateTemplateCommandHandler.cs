using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="UpdateTemplateCommand"/> by updating an existing template in the shared store.
/// Returns the updated template directly; returns a deterministic fallback when the identifier is unknown.
/// </summary>
public class UpdateTemplateCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Updates the template matching the command's identifier, or returns a placeholder when not found.
    /// </summary>
    /// <param name="command">The validated update-template command.</param>
    /// <returns>The updated template resource, or a placeholder when the identifier is unknown.</returns>
    public TemplateApiResource Handle(UpdateTemplateCommand command)
    {
        return store.Update(command.Id, command.Name!, command.HtmlSource ?? string.Empty, command.PlainTextSource ?? string.Empty)
            ?? new TemplateApiResource(command.Id, command.Name!, command.HtmlSource ?? string.Empty, command.PlainTextSource ?? string.Empty);
    }
}
