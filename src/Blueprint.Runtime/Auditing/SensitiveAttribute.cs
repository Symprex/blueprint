namespace Blueprint.Auditing;

/// <summary>
/// Marks a property as containing sensitive data (PII, credentials, financial details) that
/// audit sinks and logging pipelines should redact or mask when persisting operation records.
/// </summary>
/// <remarks>
/// <para>
/// The source generator discovers this attribute at compile time and populates
/// <see cref="SensitivePropertyMetadata"/> on the generated descriptor so that runtime audit
/// sinks can inspect which properties need redaction without runtime reflection.
/// </para>
/// <para>
/// This attribute does NOT remove the property from HTTP/OpenAPI contracts or affect serialization.
/// It purely informs observability and compliance tooling about data classification.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class SensitiveAttribute : Attribute;
