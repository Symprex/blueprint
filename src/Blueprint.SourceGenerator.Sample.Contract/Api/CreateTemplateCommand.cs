using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Creates a new template in the system. Demonstrates the generated validation pipeline
/// by requiring a name and enforcing length constraints on content fields.
/// </summary>
[RootLink("/templates")]
[HttpPost]
[MustBeAuthenticated]
public partial class CreateTemplateCommand : ICommand<TemplateApiResource>
{
    /// <summary>
    /// The name of the template to create. Required and limited to 200 characters.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>
    /// The HTML source content of the template. Limited to 50000 characters.
    /// </summary>
    [MaxLength(50000)]
    public string? HtmlSource { get; set; }

    /// <summary>
    /// The plain text source content of the template. Limited to 10000 characters.
    /// </summary>
    [MaxLength(10000)]
    public string? PlainTextSource { get; set; }
}
