namespace Blueprint.SourceGenerator.Modules.Authentication;

/// <summary>
/// Descriptor planning metadata emitted by <see cref="AuthenticationModule"/> when an operation
/// carries <c>[MustBeAuthenticated]</c>. Consumed by <c>HttpResponseMetadataModule</c> to project
/// the 401 response into the operation's HTTP response metadata.
/// </summary>
internal sealed record AuthenticationDescriptorRegistration;
