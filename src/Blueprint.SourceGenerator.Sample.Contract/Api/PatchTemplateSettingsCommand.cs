using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Partially updates a template's display settings. Demonstrates the <see cref="HttpPatchAttribute"/>
/// for PATCH semantics, <see cref="GreaterThanPropertyAttribute"/> for cross-property comparison
/// (maximum weight must exceed minimum weight), and <see cref="RangeAttribute"/> with double
/// bounds for fine-grained numeric validation.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/settings")]
[HttpPatch]
[FromResource(typeof(CreateTemplateCommand))]
[Description("Partially update a template's display settings.")]
public partial class PatchTemplateSettingsCommand : ICommand<ResourceEventResult<TemplateApiResource>>
{
    /// <summary>
    /// The identifier of the template whose settings are being patched (bound from the route).
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }

    /// <summary>
    /// Optional display opacity for the template preview. Demonstrates <see cref="RangeAttribute"/>
    /// with <c>double</c> bounds: the generator emits an inline <c>&lt;</c> / <c>&gt;</c> comparison
    /// at build time instead of falling back to runtime validation.
    /// </summary>
    [Range(0.0, 1.0)]
    [Description("Display opacity for the template preview (0.0 = fully transparent, 1.0 = fully opaque).")]
    public double? Opacity { get; set; }

    /// <summary>
    /// The minimum display weight for the template. Used together with <see cref="MaxWeight"/>
    /// to demonstrate <see cref="GreaterThanPropertyAttribute"/> cross-property validation:
    /// <see cref="MaxWeight"/> must be strictly greater than this value.
    /// </summary>
    [Range(0, 100)]
    [Description("Minimum display weight (0-100).")]
    public int? MinWeight { get; set; }

    /// <summary>
    /// The maximum display weight for the template. Demonstrates <see cref="GreaterThanPropertyAttribute"/>:
    /// the generator emits an inline <c>&lt;=</c> comparison against <see cref="MinWeight"/> at build time,
    /// including a null guard when both properties are nullable.
    /// </summary>
    [Range(0, 100)]
    [GreaterThanProperty(nameof(MinWeight))]
    [Description("Maximum display weight (0-100). Must be greater than MinWeight.")]
    public int? MaxWeight { get; set; }

    /// <summary>
    /// Optional CSS class name override for custom styling.
    /// </summary>
    [MaxLength(50)]
    [RegularExpression(@"^[a-zA-Z][a-zA-Z0-9\-_]*$")]
    [Description("Optional CSS class name for custom template styling.")]
    public string? CssClassName { get; set; }
}
