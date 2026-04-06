namespace Blueprint.Model.Attributes;

/// <summary>
/// Explicitly marks the property as body-owned. The property will be populated from the JSON request body
/// using the camelCase form of its name for field matching.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class FromBodyAttribute : Attribute;
