using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Searches templates by name with pagination. Demonstrates query-string binding
/// for non-route parameters, optional pagination defaults, public access via [AllowAnonymous],
/// <see cref="FromHeaderAttribute"/> binding for correlation IDs,
/// and <see cref="FromCookieAttribute"/> binding for user preferences.
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
    /// Must be greater than 0 and less than 1001 (maximum 1000 pages).
    /// </summary>
    [GreaterThan(0)]
    [LessThan(1001)]
    [Description("The 1-based page number for pagination.")]
    public int Page { get; set; } = 1;

    /// <summary>
    /// The page size for pagination. Defaults to 20, maximum 100.
    /// </summary>
    [Range(1, 100)]
    [Description("Number of results per page (1-100).")]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// The sort order for search results. Defaults to <see cref="TemplateSortOrder.Name"/>.
    /// Demonstrates enum property rendering as an OpenAPI <c>enum</c> array.
    /// </summary>
    [Description("The sort order for search results.")]
    public TemplateSortOrder SortBy { get; set; } = TemplateSortOrder.Name;

    /// <summary>
    /// An optional correlation ID provided via the <c>X-Correlation-Id</c> request header.
    /// Demonstrates <see cref="FromHeaderAttribute"/> binding for cross-system trace correlation.
    /// The handler echoes this value for observability integration.
    /// </summary>
    [FromHeader("X-Correlation-Id")]
    [Description("Correlation ID for cross-system trace correlation.")]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Optional tag filters to restrict results to templates matching any of the given tags.
    /// Demonstrates array query parameter binding with OpenAPI <c>style: form, explode: true</c>
    /// serialization (e.g. <c>?tags=draft&amp;tags=approved</c>).
    /// </summary>
    [Description("Filter templates by tags (repeated query parameter).")]
    public string[]? Tags { get; set; }

    /// <summary>
    /// An optional user-preference cookie that stores the preferred locale for template content.
    /// Demonstrates <see cref="FromCookieAttribute"/> binding for reading per-request state from cookies.
    /// </summary>
    [FromCookie("pref-locale")]
    [Description("Preferred locale from a user-preference cookie.")]
    public string? PreferredLocale { get; set; }
}
