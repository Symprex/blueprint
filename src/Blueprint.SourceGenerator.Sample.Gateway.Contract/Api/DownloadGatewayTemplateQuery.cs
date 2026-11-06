using System.ComponentModel;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;

/// <summary>
/// Gateway query that downloads a template's content from the upstream template service. Demonstrates a streamed
/// forward: the upstream file is passed through as a <see cref="ProxyResult"/>, never buffered in the gateway.
/// </summary>
[ApiOperations("GatewayTemplates")]
[RootLink("/templates/{Id}/download")]
[HttpGet]
[Description("Download a template's content from the upstream template service via the gateway.")]
public partial class DownloadGatewayTemplateQuery : IQuery<ProxyResult>
{
    /// <summary>
    /// The identifier of the upstream template to download.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }
}
