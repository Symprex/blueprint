namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks a property as intentionally excluded from structural comparison so transient or transport-only state does not
/// affect equality-style checks.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DoNotCompareAttribute : Attribute;
