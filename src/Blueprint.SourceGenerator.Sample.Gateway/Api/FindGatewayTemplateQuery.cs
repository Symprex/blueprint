using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Finds a single template by an identifier given in the query string. The inner <see cref="GetTemplateQuery"/> routes by
/// <c>{Id}</c>, which this route lacks: the contributed <c>Id</c> is read from the query string and copied into the inner
/// operation, so the forward still sends <c>/templates/{Id}</c> (Symprex/blueprint#161).
/// </summary>
[Expose<GetTemplateQuery>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[RootLink("/templates/find")]
[HttpGet]
[Description("Find a single template by an identifier in the query string, via the gateway.")]
public partial class FindGatewayTemplateQuery : IQuery<Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>;
