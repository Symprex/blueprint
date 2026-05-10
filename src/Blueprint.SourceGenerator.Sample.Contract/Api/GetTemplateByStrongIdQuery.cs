using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets a single template by its strongly-typed <see cref="TemplateId"/>. Demonstrates
/// <see cref="IParsable{TSelf}"/> route parameter binding: the generated pipeline calls
/// <c>TemplateId.TryParse</c> directly instead of generic string conversion, returning
/// HTTP 400 automatically if the GUID is malformed.
/// </summary>
/// <remarks>
/// Also demonstrates <see cref="FromQueryAttribute"/> with a nullable IParsable type
/// (<c>TemplateId?</c>) for optional query-parameter filtering.
/// </remarks>
[ApiOperations("Templates")]
[RootLink("/templates/by-id/{Id}")]
[HttpGet]
[AllowAnonymous]
[Description("Get a template by its strongly-typed identifier.")]
public partial class GetTemplateByStrongIdQuery : IQuery<Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>
{
    /// <summary>
    /// The strongly-typed template identifier parsed from the route segment.
    /// Demonstrates IParsable&lt;T&gt; route binding with automatic 400 on malformed input.
    /// </summary>
    [Description("The strongly-typed template identifier (GUID format).")]
    public required TemplateId Id { get; set; }

    /// <summary>
    /// An optional parent template ID for inheritance lookups. Demonstrates nullable
    /// IParsable&lt;T&gt; query-parameter binding — omitting the parameter leaves the value null.
    /// </summary>
    [FromQuery]
    [Description("Optional parent template ID for inheritance resolution.")]
    public TemplateId? ParentId { get; set; }
}
