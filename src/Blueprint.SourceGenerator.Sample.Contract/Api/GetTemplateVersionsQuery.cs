using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets the version history for a specific template. Demonstrates nested routes,
/// sub-resource relationships, and performance-metrics opt-out for frequently-polled endpoints.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/versions")]
[HttpGet]
[FromResource(typeof(GetTemplatesQuery))]
[DoNotRecordPerformanceMetrics]
public partial class GetTemplateVersionsQuery : IQuery<TemplateVersionApiResource[]>
{
    /// <summary>
    /// The identifier of the template whose versions to retrieve.
    /// </summary>
    public required string Id { get; set; }
}
