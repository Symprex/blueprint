using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Retrieves the change history for a template within an optional date range. Demonstrates:
/// <list type="bullet">
///   <item><see cref="LessThanPropertyAttribute"/> for cross-property date range validation (StartDate must be before EndDate)</item>
///   <item><see cref="RequiredIfNotAttribute"/> for conditional validation (EndDate is required when StartDate is provided)</item>
///   <item><see cref="StringLengthAttribute"/> for combined min/max string length in a single attribute</item>
/// </list>
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/history")]
[HttpGet]
[MustBeAuthenticated]
[Description("Get the change history for a template, optionally filtered by date range.")]
public partial class GetTemplateHistoryQuery : IQuery<TemplateVersionApiResource[]>
{
    /// <summary>
    /// The identifier of the template whose history to retrieve (bound from the route).
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }

    /// <summary>
    /// Optional start of the date range filter. When provided, <see cref="EndDate"/> becomes required
    /// and must be before <see cref="EndDate"/>.
    /// </summary>
    [LessThanProperty(nameof(EndDate))]
    [Description("The start of the date range filter (inclusive).")]
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// End of the date range filter. Required when <see cref="StartDate"/> is provided — uses
    /// <see cref="RequiredIfNotAttribute"/> to enforce "required unless StartDate is null".
    /// </summary>
    [RequiredIfNot(nameof(StartDate), null)]
    [Description("The end of the date range filter (inclusive). Required when StartDate is provided.")]
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Optional filter to restrict history entries to a specific author. Demonstrates
    /// <see cref="StringLengthAttribute"/> combining min and max in a single attribute.
    /// </summary>
    [StringLength(100, MinimumLength = 2)]
    [Description("Filter history by author name (2-100 characters).")]
    public string? AuthorFilter { get; set; }
}


