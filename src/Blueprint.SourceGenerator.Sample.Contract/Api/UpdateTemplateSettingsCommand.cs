using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Updates settings for a template within a specific tenant context. Demonstrates the
/// 3-parameter <c>[Resource]</c> attribute with a hierarchical resource key template
/// that binds both operation properties and auth claim tokens for instance-level authorization.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/tenants/{TenantId}/templates/{Id}/settings")]
[HttpPut]
[Resource("template", "write", "/Tenants/{Auth.AccountId}/Templates/{Id}")]
[Description("Update template settings within a tenant-scoped resource key.")]
public partial class UpdateTemplateSettingsCommand : ICommand<OkResult>
{
    /// <summary>
    /// The tenant identifier (bound from the route, but the resource key uses Auth:AccountId for authorization).
    /// </summary>
    [Description("The tenant context for this operation.")]
    public required string TenantId { get; set; }

    /// <summary>
    /// The template identifier (bound from both route and resource key template).
    /// </summary>
    [Description("The unique identifier of the template to update.")]
    public required string Id { get; set; }

    /// <summary>
    /// The new display name for the template.
    /// </summary>
    [Description("The new display name.")]
    public string? DisplayName { get; set; }
}
