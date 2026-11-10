using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets a single template within a tenant. Demonstrates a tenant-scoped read: <c>TenantIsolationApiAuthoriser</c> answers
/// 403 unless the caller's account is the tenant, so a gateway reaches it with a tenant token rather than the caller's own.
/// It reads the same store as <see cref="GetTemplateQuery"/>, so it requires the same <c>template:read</c> as well.
/// An unknown identifier is a thrown <c>not_found</c> problem, not a declared result.
/// </summary>
[ApiOperations("Templates")]
[FromResource(typeof(GetTemplatesQuery))]
[RootLink("/tenants/{TenantId}/templates/{Id}")]
[HttpGet]
[Description("Retrieve a single template within a tenant.")]
public partial class GetTenantTemplateQuery : IQuery<TemplateApiResource>
{
    /// <summary>
    /// The tenant the template belongs to, which must be the caller's account.
    /// </summary>
    [Description("The tenant the template belongs to.")]
    public required string TenantId { get; set; }

    /// <summary>
    /// The identifier of the template to retrieve.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }
}
