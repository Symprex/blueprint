using System.ComponentModel;
using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets the version history for a specific template. Demonstrates nested routes,
/// sub-resource relationships, and performance-metrics opt-out for frequently-polled endpoints.
/// Marked obsolete to demonstrate OpenAPI <c>deprecated</c> rendering.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/versions")]
[HttpGet]
[FromResource(typeof(GetTemplatesQuery))]
[DoNotRecordPerformanceMetrics]
[Description("Get the version history for a template.")]
[Obsolete("Use the new paginated template history endpoint instead.")]
public partial class GetTemplateVersionsQuery : IQuery<TemplateVersionApiResource[]>
{
    /// <summary>
    /// The identifier of the template whose versions to retrieve.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }
}
