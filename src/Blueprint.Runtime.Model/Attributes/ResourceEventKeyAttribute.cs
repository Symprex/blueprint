namespace Blueprint.Model.Attributes;

/// <summary>
/// Marks a constructor parameter in a resource-event record as the explicit resource identity key. Required when the
/// event does not carry a full resource body from which identity can be derived (key-only or partial-body events).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class ResourceEventKeyAttribute : Attribute;
