using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets the notification rules configured for a template. The operation itself declares no conditional
/// validator; it exists so <see cref="TemplateNotificationRuleApiResource"/> reaches the document as a
/// schema component referenced by <c>items.$ref</c> from the response body.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/notification-rules")]
[HttpGet]
[FromResource(typeof(GetTemplatesQuery))]
[Description("Get the notification rules configured for a template.")]
public partial class GetTemplateNotificationRulesQuery : IQuery<TemplateNotificationRuleApiResource[]>
{
    /// <summary>
    /// The identifier of the template whose notification rules to retrieve.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }
}
