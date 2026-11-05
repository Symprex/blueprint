using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Finds the copies of a template: a GET declared in this assembly, whose other properties are sent as its query string.
/// Called through <see cref="LocalOperationsApi"/>.
/// </summary>
[RootLink("/templates/{Id}/copies/search")]
[HttpGet]
[AllowAnonymous]
public partial class FindTemplateCopiesQuery : IQuery<TemplateApiResource[]>
{
    /// <summary>
    /// Gets or sets the identifier of the template whose copies are found.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the start of the copies' names.
    /// </summary>
    public string? NameStartsWith { get; set; }
}
