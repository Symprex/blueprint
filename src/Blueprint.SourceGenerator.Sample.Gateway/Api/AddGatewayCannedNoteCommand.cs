using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Adds one of a fixed set of notes within the caller's tenant. Minimal mode with a <c>[Required]</c> inner property
/// filled by the map hook (#175): the gateway hides <see cref="AddTenantNoteCommand.Text"/>, and <see cref="OnMap"/> sets
/// it from the <see cref="Kind"/> the caller chose. The assembly's <c>[GatewayBind]</c> fills the tenant.
/// </summary>
/// <remarks>
/// An unknown kind leaves the text null on purpose, to show the generated forward refusing to send a required property
/// its hook left null rather than leave it to the upstream's validation. A real gateway would refuse the kind itself.
/// </remarks>
[Expose<AddTenantNoteCommand>]
[Hide(nameof(AddTenantNoteCommand.Text))]
[ApiOperations("GatewayTenantNotes")]
[MustBeAuthenticated]
[RootLink("/canned-notes")]
[HttpPost]
[Description("Add a canned note within the caller's tenant.")]
public partial class AddGatewayCannedNoteCommand : ICommand<TenantNoteApiResource>
{
    /// <summary>
    /// The notes a caller can choose from, by kind.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CannedTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["welcome"] = "Welcome aboard.",
    };

    /// <summary>
    /// Which canned note to add: the gateway's own property, which the inner operation does not have.
    /// </summary>
    [Description("Which canned note to add.")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Runs after the generated copy and bindings, before the note is sent: sets the text of the chosen kind.
    /// </summary>
    /// <param name="inner">The note about to be sent.</param>
    internal void OnMap(AddTenantNoteCommand inner)
    {
        inner.Text = CannedTexts.GetValueOrDefault(this.Kind)!;
    }
}
