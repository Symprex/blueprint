using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Adds a note within a tenant the gateway names by its own key. The gateway declares <see cref="TenantId"/> as a
/// <see cref="GatewayTenantKey"/>, which is not the inner <see cref="AddTenantNoteCommand.TenantId"/>'s type, so the
/// declaration is the gateway's own: <c>[Hide]</c> leaves the inner property, which the inner route carries, to
/// <see cref="OnMap"/> (Symprex/blueprint#187).
/// </summary>
[Expose<AddTenantNoteCommand>]
[Hide(nameof(AddTenantNoteCommand.TenantId))]
[ApiOperations("GatewayTenantNotes")]
[MustBeAuthenticated]
[RootLink("/keyed-tenants/{TenantId}/notes")]
[HttpPost]
[Description("Add a note within a tenant named by the gateway's key.")]
public partial class AddGatewayKeyedTenantNoteCommand : ICommand<TenantNoteApiResource>
{
    /// <summary>
    /// The tenant the note belongs to, as the gateway names it.
    /// </summary>
    [Description("The tenant the note belongs to, as the gateway names it.")]
    public GatewayTenantKey TenantId { get; set; }

    /// <summary>
    /// Gives the inner command the tenant the gateway's key names.
    /// </summary>
    /// <param name="inner">The note about to be sent.</param>
    internal void OnMap(AddTenantNoteCommand inner)
    {
        inner.TenantId = this.TenantId.Value;
    }
}
