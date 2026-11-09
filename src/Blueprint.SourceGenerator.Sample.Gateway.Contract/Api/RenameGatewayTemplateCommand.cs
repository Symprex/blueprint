using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;

/// <summary>
/// Gateway command that renames a template in the upstream template service. Demonstrates
/// <see cref="TranslateErrorAttribute"/>: the upstream's <c>not_found</c> problem is answered as this gateway's own
/// <see cref="TemplateMissing"/> problem rather than passed through.
/// </summary>
[ApiOperations("GatewayTemplates")]
[RootLink("/templates/{Id}")]
[HttpPut]
[TranslateError("not_found", nameof(TemplateMissing))]
[Description("Rename a template in the upstream template service via the gateway.")]
public partial class RenameGatewayTemplateCommand : ICommand<GatewayTemplateResource>
{
    /// <summary>
    /// Answers an upstream <c>not_found</c> problem: the template to rename does not exist.
    /// </summary>
    public static readonly ApiExceptionFactory TemplateMissing = new(
        "Gateway template missing",
        "gateway_template_missing",
        HttpStatusCode.NotFound);

    /// <summary>
    /// The identifier of the upstream template to rename.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }

    /// <summary>
    /// The template's new display name.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    [Description("The new display name for the template.")]
    public required string Name { get; set; }

    /// <summary>
    /// The template's HTML source, which the upstream requires on every update.
    /// </summary>
    [Required]
    [Description("The HTML source content of the template.")]
    public required string HtmlSource { get; set; }

    /// <summary>
    /// The template's plain-text source. The upstream update replaces every content field, so the gateway forwards this
    /// one too: left out, the upstream stores the template with no plain text.
    /// </summary>
    [MaxLength(10000)]
    [Description("The plain text content of the template.")]
    public string? PlainTextSource { get; set; }
}
