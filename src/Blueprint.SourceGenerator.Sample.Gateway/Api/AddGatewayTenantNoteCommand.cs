using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Adds a note within the caller's tenant. Minimal mode with D11's app tier only: the assembly's
/// <c>[GatewayBind("TenantId", FromClaim = "tenant_id")]</c> fills the inner <see cref="AddTenantNoteCommand.TenantId"/>
/// from the caller's claim, so it is neither in this operation's shape nor settable by its caller.
/// </summary>
[Expose<AddTenantNoteCommand>]
[ApiOperations("GatewayTenantNotes")]
[MustBeAuthenticated]
[RootLink("/tenant-notes")]
[HttpPost]
[Description("Add a note within the caller's tenant.")]
public partial class AddGatewayTenantNoteCommand : ICommand<TenantNoteApiResource>;
