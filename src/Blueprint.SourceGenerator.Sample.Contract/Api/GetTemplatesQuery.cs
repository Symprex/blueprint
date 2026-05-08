using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets templates that can be presented to user's for quickly creating signatures.
/// </summary>
[ApiOperations("Templates", Description = "Email template management including CRUD, publishing, and previewing.")]
[Resource("template", "read")]
[SelfLink(typeof(TemplateListApiResource), "/templates")]
[HttpGet]
[Description("List all available templates.")]
public partial class GetTemplatesQuery : IQuery<TemplateApiResource[]>;
