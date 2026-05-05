using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Searches templates by name with pagination. Demonstrates query-string binding
/// for non-route parameters and optional pagination defaults.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/search")]
[HttpGet]
[MustBeAuthenticated]
public partial class SearchTemplatesQuery : IQuery<TemplateApiResource[]>
{
    /// <summary>
    /// The search term to filter templates by name (case-insensitive substring match).
    /// </summary>
    [Required]
    [MinLength(1)]
    public string? Q { get; set; }

    /// <summary>
    /// The page number for pagination (1-based). Defaults to 1.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// The page size for pagination. Defaults to 20, maximum 100.
    /// </summary>
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
