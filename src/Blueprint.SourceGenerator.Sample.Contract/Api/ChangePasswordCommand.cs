using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Changes a user's password with confirmation. Demonstrates the <see cref="CompareAttribute"/>
/// fast-path: the generator emits an inline <c>!object.Equals(ConfirmNewPassword, NewPassword)</c>
/// check instead of falling back to runtime reflection, while the error message still comes from
/// the framework's <see cref="CompareAttribute"/> formatter.
/// </summary>
[ApiOperations("Account")]
[RootLink("/account/password")]
[HttpPut]
[MustBeAuthenticated]
[DoNotAudit]
[Description("Change the authenticated user's password.")]
public partial class ChangePasswordCommand : ICommand<NoResultOperationResult>
{
    /// <summary>
    /// The user's current password for verification.
    /// </summary>
    [Required]
    [Description("The current password.")]
    public string? CurrentPassword { get; set; }

    /// <summary>
    /// The desired new password. Must be between 8 and 128 characters.
    /// Demonstrates <see cref="LengthAttribute"/> (.NET 8) fast-path: the generator emits a
    /// single inline <c>prop.Length &lt; 8 || prop.Length &gt; 128</c> check instead of two
    /// separate min/max fallback calls.
    /// </summary>
    [Required]
    [Length(8, 128)]
    [Description("The new password.")]
    public string? NewPassword { get; set; }

    /// <summary>
    /// Confirmation of the new password. Must match <see cref="NewPassword"/> exactly.
    /// </summary>
    [Required]
    [Compare(nameof(NewPassword))]
    [Description("Re-enter the new password for confirmation.")]
    public string? ConfirmNewPassword { get; set; }
}
