namespace Blueprint;

/// <summary>
/// Marks an operation as unexposed, meaning it will not be included in API documentation (the OpenAPI document and
/// the <c>x-links</c> of the resources it links from), link generation, or the generated typed HTTP client. Typically
/// used for internal operations that should not appear in the public API surface.
/// </summary>
/// <remarks>
/// An unexposed operation that declares a URL is still routed, so internal callers — other services, management
/// tooling — can reach it over HTTP. It is hidden from the published contract, not made unreachable; authorise it
/// accordingly.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class UnexposedOperationAttribute : Attribute
{
}
