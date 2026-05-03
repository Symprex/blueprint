using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Updates an existing template's metadata and content. Demonstrates a URL-parameterized command
/// with body binding at the same route pattern as the GET query but with a different HTTP method.
/// </summary>
[RootLink("/templates/{Id}")]
[HttpPut]
[MustBeAuthenticated]
public partial class UpdateTemplateCommand : ICommand<TemplateApiResource>
{
    /// <summary>
    /// The identifier of the template to update (bound from the route).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The updated name for the template. Required and limited to 200 characters.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>
    /// The updated HTML source content of the template.
    /// </summary>
    [MaxLength(50000)]
    public string? HtmlSource { get; set; }

    /// <summary>
    /// The updated plain text source content of the template.
    /// </summary>
    [MaxLength(10000)]
    public string? PlainTextSource { get; set; }
}
