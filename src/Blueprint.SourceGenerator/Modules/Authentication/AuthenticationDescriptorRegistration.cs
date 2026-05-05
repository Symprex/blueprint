namespace Blueprint.SourceGenerator.Modules.Authentication;

/// <summary>
/// Descriptor planning metadata emitted by <see cref="AuthenticationModule"/> when an operation
/// carries an access-mode attribute. The <see cref="RequiresIdentityCheck"/> flag distinguishes
/// operations that enforce an identity gate (producing a 401 short-circuit) from those marked
/// <c>[AllowAnonymous]</c> which carry explicit access-mode metadata without runtime enforcement.
/// </summary>
/// <param name="RequiresIdentityCheck">
/// <c>true</c> when the operation requires an authenticated identity (e.g. <c>[MustBeAuthenticated]</c>);
/// <c>false</c> when the operation explicitly allows anonymous access.
/// </param>
internal sealed record AuthenticationDescriptorRegistration(bool RequiresIdentityCheck);
