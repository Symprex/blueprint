using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.Tasks;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles template publish commands. Returns <see cref="ConflictResult{T}"/> (409) when the
/// template is already published, or a <c>template.published</c> resource-event result (201)
/// on first publish — demonstrating the conflict/success discriminated union pattern.
/// Also demonstrates enqueueing a background task via <see cref="IBackgroundTaskScheduler"/>
/// and publishing domain events through the message outbox (like Signature365's Rebus pattern).
/// </summary>
public sealed class PublishTemplateCommandHandler(TemplateStore store, IBackgroundTaskScheduler taskScheduler)
{
    /// <summary>
    /// Publishes the specified template, or signals a conflict when already published.
    /// </summary>
    /// <param name="command">The publish command with the template identifier.</param>
    /// <returns>A resource-event result on success, or a conflict result when re-publishing.</returns>
    public Results<ResourceEventResult<TemplateApiResource>, ConflictResult<TemplateApiResource>> Handle(PublishTemplateCommand command)
    {
        var template = store.GetById(command.Id)
            ?? throw new InvalidOperationException($"Template '{command.Id}' not found.");

        if (store.IsPublished(command.Id))
        {
            return new ConflictResult<TemplateApiResource>(template);
        }

        store.MarkPublished(command.Id);

        // Enqueue a notification task — dispatched by the pipeline after successful execution
        taskScheduler.Enqueue(new SendTemplateSharedNotificationTask { TemplateId = command.Id });

        // Publish a domain event through the message outbox — the generated pipeline
        // commits the outbox after successful handler execution, ensuring messages are
        // only published when the command succeeds (like Signature365's Rebus pattern)
        InMemoryMessageOutbox.Current?.Publish(new TemplatePublishedEvent(command.Id, template.Name));

        return (ResourceEventResult<TemplateApiResource>)new TemplateResourceEvents.Published(template);
    }

    /// <summary>
    /// Domain event published when a template is successfully published.
    /// In a real application, this would be a Rebus message consumed by other services.
    /// </summary>
    /// <param name="TemplateId">The identifier of the published template.</param>
    /// <param name="TemplateName">The name of the published template.</param>
    public sealed record TemplatePublishedEvent(string TemplateId, string TemplateName);
}
