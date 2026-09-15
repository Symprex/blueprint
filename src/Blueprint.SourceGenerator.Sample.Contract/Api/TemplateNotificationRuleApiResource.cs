using System.ComponentModel;
using System.Text.Json.Serialization;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// A single notification rule for a template: an on/off switch plus the two fields that switch makes
/// mandatory. This is a nested DTO rather than an operation — it reaches the document only as the element
/// type of <see cref="GetTemplateNotificationRulesQuery"/>'s response, so its schema component is built from
/// serializer metadata rather than from a request body.
/// </summary>
/// <remarks>
/// The conditionals below are the reason this type exists. They belong to <em>this</em> schema, not to the
/// response body that <c>$ref</c>s it, so the projected document must carry the <c>allOf</c>/<c>if</c>/<c>then</c>
/// pair on this component. The shape mirrors the settings DTOs a consuming app shares between its forms, where
/// a single flag gates the fields beneath it.
/// </remarks>
public class TemplateNotificationRuleApiResource
{
    /// <summary>
    /// Gets or sets a value indicating whether this notification rule is active. When true,
    /// <see cref="RecipientEmail"/> and <see cref="Subject"/> both become required.
    /// </summary>
    [Description("Whether this notification rule is active.")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the address notified when the rule fires. Required when <see cref="Enabled"/> is true.
    /// </summary>
    /// <remarks>
    /// The <c>[JsonPropertyName]</c> is deliberate. A conditional validator names a CLR property while the schema
    /// declares a JSON one, so this property is the reference app's only case where the two differ and a projection
    /// that skipped the resolution would be visible in the document.
    /// </remarks>
    [RequiredIf(nameof(Enabled), true)]
    [JsonPropertyName("recipient")]
    [Description("The address notified when the rule fires. Required when Enabled is true.")]
    public string? RecipientEmail { get; set; }

    /// <summary>
    /// Gets or sets the subject line used for the notification. Required when <see cref="Enabled"/> is true.
    /// </summary>
    [RequiredIf(nameof(Enabled), true)]
    [Description("The subject line used for the notification. Required when Enabled is true.")]
    public string? Subject { get; set; }
}
