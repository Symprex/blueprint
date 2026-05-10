namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks a property as <c>writeOnly</c> in the OpenAPI schema, meaning it appears in
/// request bodies but should not be included in response bodies (e.g. a password field).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OpenApiWriteOnlyAttribute : Attribute;
