using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets a paged list of templates with support for pagination parameters.
/// Demonstrates the <see cref="PagedApiResource{T}"/> pattern for operations
/// that return large result sets with total counts.
/// </summary>
[ApiOperations("Templates")]
[Resource("template", "read")]
[RootLink("/templates/paged")]
[HttpGet]
[Description("List templates with pagination.")]
public partial class GetPagedTemplatesQuery : IQuery<PagedApiResource<TemplateApiResource>>
{
    /// <summary>
    /// The 1-based page number to retrieve.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    /// <summary>
    /// The number of items per page.
    /// </summary>
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
