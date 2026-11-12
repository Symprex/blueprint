using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Adds a note on a partner's behalf. Minimal mode with every tier of D11: the operation binds the tenant from the
/// caller's <c>account_id</c> claim in place of the assembly's <c>tenant_id</c> binding, fixes the source, hides the staff
/// note, and tidies and guards the text in <see cref="OnMapAsync"/>.
/// </summary>
[Expose<AddTenantNoteCommand>]
[Bind(nameof(AddTenantNoteCommand.TenantId), FromClaim = "account_id")]
[Bind(nameof(AddTenantNoteCommand.Source), Value = "partner-portal")]
[Hide(nameof(AddTenantNoteCommand.InternalNote))]
[ApiOperations("GatewayTenantNotes")]
[MustBeAuthenticated]
[RootLink("/partner-notes")]
[HttpPost]
[Description("Add a note on a partner's behalf.")]
public partial class AddGatewayPartnerNoteCommand : ICommand<TenantNoteApiResource>
{
    /// <summary>
    /// The text no partner may send.
    /// </summary>
    public const string BlockedText = "blocked";

    /// <summary>
    /// Runs after the generated copy and bindings, before the note is sent: trims the text, and refuses a blocked one. It
    /// reads nothing of the gateway operation, so it is static, which the generated forward calls on the type (#176).
    /// </summary>
    /// <param name="inner">The note about to be sent.</param>
    /// <param name="logger">Injected as a handler's parameter would be.</param>
    /// <returns>A completed task.</returns>
    internal static ValueTask OnMapAsync(AddTenantNoteCommand inner, ILogger<AddGatewayPartnerNoteCommand> logger)
    {
        inner.Text = inner.Text.Trim();

        if (inner.Text == BlockedText)
        {
            logger.LogInformation("Refused a blocked partner note for tenant {TenantId}", inner.TenantId);
            throw new ForbiddenException("Partners may not send this note.");
        }

        return ValueTask.CompletedTask;
    }
}
