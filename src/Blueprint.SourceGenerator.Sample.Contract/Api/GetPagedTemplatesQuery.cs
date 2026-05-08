using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets a paged list of templates with support for pagination and ordering.
/// Demonstrates the <see cref="OrderedPagedQuery{T}"/> base class pattern
/// that Signature365 uses extensively for paged query operations.
/// </summary>
[ApiOperations("Templates")]
[Resource("template", "read")]
[RootLink("/templates/paged")]
[HttpGet]
[Description("List templates with pagination and optional ordering.")]
public partial class GetPagedTemplatesQuery : OrderedPagedQuery<TemplateApiResource>, IQuery<PagedApiResource<TemplateApiResource>>
{
    /// <summary>
    /// Optional name filter to search templates.
    /// </summary>
    public string? NameFilter { get; set; }
}
