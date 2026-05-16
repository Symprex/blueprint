namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares a "self" link relationship that connects a resource to the operation that retrieves it.
/// This is a convenience shorthand for <c>[Link(resourceType, routePattern, Rel = "self")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class SelfLinkAttribute : LinkAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SelfLinkAttribute"/> class.
    /// </summary>
    /// <param name="resourceType">The type of resource this self-link targets.</param>
    /// <param name="routePattern">
    /// The route pattern for this link, which may contain placeholders
    /// (e.g. <c>/users/{Id}</c> or <c>/tenants/{TenantId}/templates/{Id}</c>).
    /// </param>
    public SelfLinkAttribute(Type resourceType, string routePattern)
        : base(resourceType, routePattern, "self")
    {
    }
}
