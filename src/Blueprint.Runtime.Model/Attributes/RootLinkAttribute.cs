namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares a root link (collection endpoint) for an operation without targeting a specific resource type.
/// This is a convenience shorthand for <c>[Link(null, routePattern)]</c> and is typically used for
/// list/search endpoints that return collections rather than individual resources.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class RootLinkAttribute : LinkAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RootLinkAttribute"/> class.
    /// </summary>
    /// <param name="routePattern">
    /// The route pattern for this link, which may contain placeholders
    /// (e.g. <c>/tenants/{TenantId}/sent-emails</c>).
    /// </param>
    public RootLinkAttribute(string routePattern)
        : base(null, routePattern)
    {
    }
}
