using System.ComponentModel;
using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;

/// <summary>
/// Gateway resource representing a template fetched from the upstream template service.
/// Mirrors the key fields of the upstream <c>TemplateApiResource</c> without coupling the
/// gateway contract to the upstream project.
/// </summary>
[DisplayName("Gateway Template")]
public partial class GatewayTemplateResource : ApiResource
{
    /// <summary>
    /// The unique identifier of the template from the upstream service.
    /// </summary>
    [Description("The unique template identifier.")]
    public required string Id { get; set; }

    /// <summary>
    /// The display name of the template.
    /// </summary>
    [Description("The human-readable display name.")]
    public required string Name { get; set; }

    /// <summary>
    /// Whether the template has been archived in the upstream service.
    /// </summary>
    [Description("Whether the template has been archived.")]
    public bool IsArchived { get; set; }
}
