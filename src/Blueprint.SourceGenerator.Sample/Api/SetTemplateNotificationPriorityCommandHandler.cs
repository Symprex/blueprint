using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="SetTemplateNotificationPriorityCommand"/> by recording the priority level as a
/// key-only resource-event result (HTTP 204 No Content).
/// </summary>
public class SetTemplateNotificationPriorityCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Records the notification priority for the specified template.
    /// </summary>
    /// <param name="command">The priority command with the target identifier and level.</param>
    /// <returns>A key-only resource-event result carrying identity without a response body.</returns>
    public KeyOnlyResourceEventResult Handle(SetTemplateNotificationPriorityCommand command)
    {
        _ = store.GetById(command.Id);

        return new TemplateResourceEvents.NotificationPriorityChanged(command.Id);
    }
}
