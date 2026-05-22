using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="ChangePasswordCommand"/>. In a real application this would verify the current
/// password and persist the new one; here it simply returns success to demonstrate the
/// <see cref="System.ComponentModel.DataAnnotations.CompareAttribute"/> fast-path validation pipeline.
/// </summary>
public class ChangePasswordCommandHandler
{
    /// <summary>
    /// Processes the password change. Validation (including the Compare check) is handled
    /// entirely by the generated pipeline before this method is reached.
    /// </summary>
    /// <param name="command">The validated password-change command.</param>
    /// <returns>A no-content success result.</returns>
    public NoResultOperationResult Handle(ChangePasswordCommand command)
    {
        // In production: verify command.CurrentPassword against the stored hash,
        // then persist the new password hash derived from command.NewPassword.
        return NoResultOperationResult.Instance;
    }
}
