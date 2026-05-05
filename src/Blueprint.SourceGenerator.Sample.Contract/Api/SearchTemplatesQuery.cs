using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Searches templates by name with pagination. Demonstrates query-string binding
/// for non-route parameters, optional pagination defaults, and public access via [AllowAnonymous].
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/search")]
[HttpGet]
[AllowAnonymous]
[DoNotAudit]
[Description("Search templates by name with pagination support.")]
public partial class SearchTemplatesQuery : IQuery<TemplateApiResource[]>
{
    /// <summary>
    /// The search term to filter templates by name (case-insensitive substring match).
    /// </summary>
    [Required]
    [MinLength(1)]
    [Description("The search term to filter templates by name.")]
    public string? Q { get; set; }

    /// <summary>
    /// The page number for pagination (1-based). Defaults to 1.
    /// </summary>
    [GreaterThan(0)]
    [Description("The 1-based page number for pagination.")]
    public int Page { get; set; } = 1;

    /// <summary>
    /// The page size for pagination. Defaults to 20, maximum 100.
    /// </summary>
    [Range(1, 100)]
    [Description("Number of results per page (1-100).")]
    public int PageSize { get; set; } = 20;
}
