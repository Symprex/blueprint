namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Descriptor planning metadata emitted by <see cref="PropertyOwnershipModule"/> that exposes the
/// full set of analyzed property binding entries. Consumed by modules that need to know both the
/// binding source and external name of each property (e.g. <c>HttpClientRequestBuilderModule</c>
/// uses this to emit camelCase query parameter names matching the server and OpenAPI contract).
/// </summary>
/// <param name="Entries">The ordered property binding entries with source and binding name information.</param>
internal sealed record PropertyBindingEntriesMetadata(
    IReadOnlyList<PropertyBindingEntriesMetadata.Entry> Entries)
{
    /// <summary>
    /// A single property's binding ownership projected for cross-module consumption.
    /// </summary>
    /// <param name="PropertyName">The CLR property name on the operation type.</param>
    /// <param name="Source">The binding source that owns this property.</param>
    /// <param name="BindingName">
    /// The external name used for binding (e.g. camelCase query name, route placeholder name, header name).
    /// Null when the CLR name is used directly.
    /// </param>
    /// <param name="FallbackSource">
    /// The source a route placeholder that is not in every one of the operation's links is also bound from, through a link
    /// that leaves it out (legacy's every-link rule); <see langword="null"/> for any other property.
    /// </param>
    internal sealed record Entry(
        string PropertyName,
        PropertyOwnershipModule.PropertyOwnershipSource Source,
        string? BindingName,
        PropertyOwnershipModule.PropertyOwnershipSource? FallbackSource = null);
}
