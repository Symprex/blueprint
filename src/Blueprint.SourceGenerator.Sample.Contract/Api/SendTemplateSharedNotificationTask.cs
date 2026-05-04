using System.ComponentModel.DataAnnotations;
using Blueprint.Tasks;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// A background task that sends a notification email when a template is shared.
/// Demonstrates that non-HTTP operations coexist with HTTP operations in the same contract assembly,
/// discovered by the same app-scan, and dispatched through the task host without HTTP involvement.
/// </summary>
public partial class SendTemplateSharedNotificationTask : IBackgroundTask
{
    /// <summary>
    /// Gets or sets the ID of the template being shared.
    /// </summary>
    [Required]
    public string? TemplateId { get; set; }

    /// <summary>
    /// Gets or sets the email address of the share recipient.
    /// </summary>
    [Required]
    [EmailAddress]
    public string? RecipientEmail { get; set; }

    /// <summary>
    /// Gets or sets an optional sender name for the notification.
    /// </summary>
    public string? SenderName { get; set; }
}
