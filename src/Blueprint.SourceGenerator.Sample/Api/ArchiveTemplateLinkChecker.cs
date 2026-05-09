using Blueprint.Authorization;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Hides the "Archive" link on templates that are already archived. Demonstrates the
/// <see cref="StateBasedLinkChecker{TOperation, TResource}"/> pattern for state-aware
/// link visibility in API responses.
/// </summary>
public class ArchiveTemplateLinkChecker : StateBasedLinkChecker<ArchiveTemplateCommand, TemplateApiResource>
{
    /// <inheritdoc />
    protected override bool IsLinkAvailableForOperation(ApiOperationContext operationContext, TemplateApiResource resource)
    {
        // Cannot archive an already-archived template.
        return !resource.IsArchived;
    }
}
