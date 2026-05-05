using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Declares the family of resource events that can be raised against a <see cref="TemplateApiResource"/>.
/// Each nested record represents a distinct event outcome with a stable event identifier.
/// </summary>
[ResourceEventFamily(typeof(TemplateApiResource), ResourceObject = "template")]
public static partial class TemplateResourceEvents
{
    /// <summary>
    /// Signals that a new template was created.
    /// </summary>
    [ResourceEventReason(ChangeType.Created, "created")]
    public sealed partial record Created(TemplateApiResource Resource);

    /// <summary>
    /// Signals that an existing template was updated.
    /// </summary>
    [ResourceEventReason(ChangeType.Updated, "updated")]
    public sealed partial record Updated(TemplateApiResource Resource);

    /// <summary>
    /// Signals that an existing template was archived (soft-deleted).
    /// </summary>
    [ResourceEventReason(ChangeType.Deleted, "archived")]
    public sealed partial record Archived([ResourceEventKey] string Key);

    /// <summary>
    /// Signals that an existing template was published (made available for use).
    /// </summary>
    [ResourceEventReason(ChangeType.Updated, "published")]
    public sealed partial record Published(TemplateApiResource Resource);

    /// <summary>
    /// Signals that a template was cloned from an existing one.
    /// </summary>
    [ResourceEventReason(ChangeType.Created, "cloned")]
    public sealed partial record Cloned(TemplateApiResource Resource);
}
