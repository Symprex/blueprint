namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Descriptor planning metadata emitted by <see cref="AuthorizationModule"/> when an operation
/// declares <c>[Resource]</c> attributes. Carries the extracted scope pairs for downstream
/// modules that need to reason about authorization requirements (e.g. manifest generation).
/// </summary>
/// <param name="RequiresResourceAuthorization">
/// <c>true</c> when the operation has declared resource authorization requirements
/// (via <c>[Resource]</c> attributes) that will require runtime authorization checks.
/// </param>
internal sealed record AuthorizationDescriptorRegistration(bool RequiresResourceAuthorization);
