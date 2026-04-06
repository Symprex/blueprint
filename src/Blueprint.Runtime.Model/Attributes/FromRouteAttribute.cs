namespace Blueprint.Model.Attributes;

/// <summary>
/// Explicitly binds the property from a route placeholder. The placeholder name must match the property name
/// in the URL template (e.g. <c>/users/{Id}</c> binds to a property named <c>Id</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class FromRouteAttribute : Attribute;
