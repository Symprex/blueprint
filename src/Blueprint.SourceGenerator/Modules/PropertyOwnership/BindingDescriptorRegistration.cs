namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Descriptor planning metadata emitted by <see cref="PropertyOwnershipModule"/> when an operation
/// has request-binding properties. Consumed by <c>HttpResponseMetadataModule</c> to project 400 and
/// 415 responses into the operation's HTTP response metadata.
/// </summary>
/// <param name="HasBodyBinding">
/// <see langword="true"/> when at least one property is bound from the request body.
/// Drives both the 400 (binding failure) and 415 (unsupported media type) response entries.
/// </param>
/// <param name="HasHeaderOrCookieBinding">
/// <see langword="true"/> when at least one property is bound from a request header or cookie.
/// Drives the 400 (binding failure) response entry alongside body binding.
/// </param>
internal sealed record BindingDescriptorRegistration(bool HasBodyBinding, bool HasHeaderOrCookieBinding);
