using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Adds a note within a tenant, and answers with the note as the service received it. The sample gateway exposes it with
/// its <c>TenantId</c> bound from the caller's claims, so that a gateway caller can neither see nor set the tenant.
/// </summary>
/// <remarks>
/// The inner service's <c>TenantIsolationApiAuthoriser</c> refuses a <c>TenantId</c> that is not the caller's account.
/// </remarks>
[ApiOperations("TenantNotes")]
[RootLink("/tenants/{TenantId}/notes")]
[HttpPost]
[Resource("template", "write")]
[Description("Add a note within a tenant.")]
public partial class AddTenantNoteCommand : ICommand<TenantNoteApiResource>
{
    /// <summary>
    /// The longest <see cref="Source"/> the service accepts.
    /// </summary>
    public const int MaxSourceLength = 50;

    /// <summary>
    /// The tenant the note belongs to.
    /// </summary>
    [Description("The tenant the note belongs to.")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The note.
    /// </summary>
    [Required]
    [Description("The note.")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Where the note came from.
    /// </summary>
    [MaxLength(MaxSourceLength)]
    [Description("Where the note came from.")]
    public string? Source { get; set; }

    /// <summary>
    /// A note for staff only.
    /// </summary>
    [Description("A note for staff only.")]
    public string? InternalNote { get; set; }
}

/// <summary>
/// A note within a tenant, as <see cref="AddTenantNoteCommand"/> received it.
/// </summary>
public partial class TenantNoteApiResource : ApiResource
{
    /// <summary>
    /// The tenant the note belongs to.
    /// </summary>
    [Description("The tenant the note belongs to.")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The note.
    /// </summary>
    [Description("The note.")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Where the note came from.
    /// </summary>
    [Description("Where the note came from.")]
    public string? Source { get; set; }

    /// <summary>
    /// A note for staff only.
    /// </summary>
    [Description("A note for staff only.")]
    public string? InternalNote { get; set; }
}
