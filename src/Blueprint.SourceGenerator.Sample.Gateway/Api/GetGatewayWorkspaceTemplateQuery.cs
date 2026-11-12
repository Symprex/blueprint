using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Retrieves a template by the workspace a caller knows it under. Minimal mode with a <c>required</c> inner property
/// filled by the map hook (#175): the gateway hides the inner <see cref="GetTenantTemplateQuery.TenantId"/>, and
/// <see cref="OnMap"/> looks the tenant up from the workspace in the route, with no placeholder binding.
/// </summary>
/// <remarks>
/// <c>[Hide]</c> of a required property is allowed only because <see cref="OnMap"/> assigns it: without that assignment
/// the build fails with BPGW035.
/// </remarks>
[Expose<GetTenantTemplateQuery>]
[Hide(nameof(GetTenantTemplateQuery.TenantId))]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[RootLink("/workspaces/{Workspace}/templates/{Id}")]
[HttpGet]
[Description("Retrieve a single template by workspace from the upstream template service via the gateway.")]
public partial class GetGatewayWorkspaceTemplateQuery : IQuery<TemplateApiResource>
{
    /// <summary>
    /// The workspaces the sample knows, and the tenant each one belongs to.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Workspaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = "tenant-alpha",
    };

    /// <summary>
    /// The caller's name for the workspace: the gateway's own property, which the inner operation does not have.
    /// </summary>
    [Description("The workspace the template belongs to.")]
    public string Workspace { get; set; } = string.Empty;

    /// <summary>
    /// Runs after the generated copy, before the query is sent: sets the tenant the workspace belongs to.
    /// </summary>
    /// <param name="inner">The query about to be sent.</param>
    /// <exception cref="NotFoundException">The workspace is not one the sample knows.</exception>
    internal void OnMap(GetTenantTemplateQuery inner)
    {
        inner.TenantId = Workspaces.GetValueOrDefault(this.Workspace)
            ?? throw new NotFoundException($"Workspace '{this.Workspace}' not found.");
    }
}
