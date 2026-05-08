using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Redirects to the external documentation for a template. Demonstrates <see cref="RedirectResult"/>
/// which sends an HTTP redirect response with a configurable status code and <c>Location</c> header.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/docs")]
[HttpGet]
[Resource("template", "read")]
[Description("Redirect to the external documentation page for a template.")]
public partial class RedirectToTemplateDocsQuery : IQuery<RedirectResult>
{
    /// <summary>
    /// The identifier of the template whose documentation to redirect to.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }
}
