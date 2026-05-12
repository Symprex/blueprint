using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;

/// <summary>
/// Gateway command that creates a new template in the upstream template service.
/// Demonstrates HTTP client POST with a JSON request body serialized from operation properties.
/// </summary>
[ApiOperations("GatewayTemplates")]
[RootLink("/templates")]
[HttpPost]
[Description("Create a new template in the upstream template service via the gateway.")]
public partial class CreateGatewayTemplateCommand : ICommand<OkResult<GatewayTemplateResource>>
{
    /// <summary>
    /// The display name for the new template.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    [Description("The display name for the new template.")]
    public required string Name { get; set; }

    /// <summary>
    /// The HTML source content for the new template.
    /// </summary>
    [Required]
    [Description("The HTML source content for the new template.")]
    public required string HtmlSource { get; set; }

    /// <summary>
    /// The plain text fallback content for the new template.
    /// </summary>
    [Required]
    [Description("The plain text fallback content for the new template.")]
    public required string PlainTextSource { get; set; }
}
