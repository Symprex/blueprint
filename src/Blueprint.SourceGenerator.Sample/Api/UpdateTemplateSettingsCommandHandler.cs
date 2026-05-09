using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="UpdateTemplateSettingsCommand"/> demonstrating an operation that uses
/// a hierarchical resource key template for instance-level authorization. The
/// <c>[Resource("template", "write", "/Tenants/{Auth:AccountId}/Templates/{Id}")]</c>
/// attribute on the command means the authorization provider receives the expanded resource key
/// with the caller's AccountId claim and the template's Id property substituted.
/// </summary>
public class UpdateTemplateSettingsCommandHandler
{
    /// <summary>
    /// Updates template settings and returns success.
    /// </summary>
    /// <param name="command">The command containing the update payload.</param>
    /// <returns>An OK result indicating settings were updated successfully.</returns>
    public OkResult Handle(UpdateTemplateSettingsCommand command)
    {
        // In a real app this would update the template's settings in the database.
        // The authorization check (with resource key template expansion) happens in the
        // generated pipeline before this handler is reached.
        return new OkResult(new { command.Id, command.DisplayName, Updated = true });
    }
}
