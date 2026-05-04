using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="SendTemplateSharedNotificationTask"/> background task by recording
/// a notification send. Demonstrates handler execution through the non-HTTP task-host pipeline.
/// </summary>
public class SendTemplateSharedNotificationTaskHandler
{
    /// <summary>
    /// Processes the notification task. In a real app this would send an email;
    /// here it completes to prove the pipeline executed.
    /// </summary>
    /// <param name="task">The validated background task.</param>
    /// <returns>A completed task indicating successful processing.</returns>
    public Task HandleAsync(SendTemplateSharedNotificationTask task)
    {
        return Task.CompletedTask;
    }
}
