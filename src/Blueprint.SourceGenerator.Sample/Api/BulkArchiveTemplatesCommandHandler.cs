using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="BulkArchiveTemplatesCommand"/> by marking the templates for archive
/// and returning a bodyless 202 Accepted. Demonstrates the fire-and-forget async pattern
/// where the caller only knows the work was accepted, with no tracking resource.
/// </summary>
public class BulkArchiveTemplatesCommandHandler(
    TemplateStore store,
    ILogger<BulkArchiveTemplatesCommandHandler> logger)
{
    /// <summary>
    /// Marks templates for archive and returns a bodyless 202.
    /// </summary>
    /// <param name="command">The validated bulk archive command.</param>
    /// <returns>A bodyless 202 Accepted status code result.</returns>
    public StatusCodeResult.Accepted Handle(BulkArchiveTemplatesCommand command)
    {
        foreach (var id in command.Ids)
        {
            store.Delete(id);
            logger.LogInformation("Archived template {TemplateId}, reason: {Reason}", id, command.Reason ?? "(none)");
        }

        logger.LogInformation("Bulk archive accepted for {Count} template(s)", command.Ids.Length);

        return StatusCodeResult.Accepted.Instance;
    }
}
