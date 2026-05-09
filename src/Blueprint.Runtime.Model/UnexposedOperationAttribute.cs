namespace Blueprint;

/// <summary>
/// Marks an operation as unexposed, meaning it will not be included in API documentation,
/// link generation, or public endpoint routing. Typically used for internal operations
/// such as background tasks or message handlers that should not appear in the public API surface.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class UnexposedOperationAttribute : Attribute
{
}
