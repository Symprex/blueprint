namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks a constructor parameter in a resource-event record as the explicit event body payload. Required when the
/// event carries a partial or alternate body type that differs from the family's primary resource type and an explicit
/// key parameter is also present.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class ResourceEventBodyAttribute : Attribute;
