namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Descriptor planning metadata emitted by <see cref="PropertyOwnershipModule"/> when an operation
/// has request-binding properties. Consumed by <c>HttpResponseMetadataModule</c> to project 400 and
/// 415 responses into the operation's HTTP response metadata.
/// </summary>
/// <param name="CanFailToBind">
/// <see langword="true"/> when a property is bound from the request body or from a header or cookie, or, beside a
/// <c>Stream</c> body, from the route or the query string. Drives the 400 (binding failure) response entry.
/// </param>
/// <param name="RejectsUnsupportedMediaType">
/// <see langword="true"/> when a property is bound from a JSON or <c>multipart/form-data</c> body, which refuses any other
/// media type; a <c>Stream</c> body reads any. Drives the 415 (unsupported media type) response entry.
/// </param>
internal sealed record BindingDescriptorRegistration(bool CanFailToBind, bool RejectsUnsupportedMediaType);
