using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets templates that can be presented to user's for quickly creating signatures.
/// </summary>
[MustBeAuthenticated]
[SelfLink(typeof(TemplateListApiResource), "/templates")]
[HttpGet]
public partial class GetTemplatesQuery : IQuery<TemplateApiResource[]>;
