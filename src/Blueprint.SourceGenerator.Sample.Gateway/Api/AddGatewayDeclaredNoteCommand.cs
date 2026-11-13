using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Adds a note within the caller's tenant through a public contract written out in full (declared mode, D10). Each
/// property takes <see cref="AddTenantNoteCommand"/>'s validation rules and docs, so neither is repeated here. The tenant
/// comes from the assembly's <c>tenant_id</c> binding, and the staff note is not part of the contract, so the forward
/// leaves it unset.
/// </summary>
[ForwardsTo<AddTenantNoteCommand>]
[ApiOperations("GatewayTenantNotes")]
[MustBeAuthenticated]
[RootLink("/declared-notes")]
[HttpPost]
[Description("Add a note through the gateway's declared contract.")]
public partial class AddGatewayDeclaredNoteCommand : ICommand<TenantNoteApiResource>
{
    /// <inheritdoc cref="AddTenantNoteCommand.Text"/>
    public string Text { get; set; } = string.Empty;

    // Renamed on the public surface, and validated by the inner property's [MaxLength].

    /// <inheritdoc cref="AddTenantNoteCommand.Source"/>
    [ForwardAs(nameof(AddTenantNoteCommand.Source))]
    public string? Channel { get; set; }
}
