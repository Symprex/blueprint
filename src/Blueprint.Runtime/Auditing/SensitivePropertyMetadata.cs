namespace Blueprint.Auditing;

/// <summary>
/// Compile-time-generated metadata identifying which properties on an operation are marked
/// <c>[Sensitive]</c>. Stored as feature data on <see cref="Model.ApiOperationDescriptor"/> via
/// <c>SetFeatureData</c> so audit sinks can discover sensitive fields without runtime reflection.
/// </summary>
/// <param name="PropertyNames">The ordered list of property names that carry the <see cref="SensitiveAttribute"/>.</param>
public sealed record SensitivePropertyMetadata(IReadOnlyList<string> PropertyNames);
