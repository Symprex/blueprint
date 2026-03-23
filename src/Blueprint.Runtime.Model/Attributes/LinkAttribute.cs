namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares a link relationship that should be attached to an operation descriptor so generated metadata can expose
/// affordances to clients without hard-coding them at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class LinkAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkAttribute"/> class.
    /// </summary>
    /// <param name="resourceType">The linked resource type when the relation targets a concrete resource payload.</param>
    /// <param name="routePattern">The route pattern that should be registered for the relation.</param>
    /// <param name="rel">The relation name to expose; when omitted, Blueprint will infer the conventional relation.</param>
    public LinkAttribute(Type? resourceType, string routePattern, string? rel = null)
    {
        this.ResourceType = resourceType;
        this.RoutePattern = routePattern;
        this.Rel = rel;
    }

    // protected LinkAttribute()
    // {
    // }

    /// <summary>
    /// Gets the route pattern that should be used when generating the relation URL.
    /// </summary>
    public string RoutePattern { get; }

    /// <summary>
    /// Gets the resource type targeted by the relation, when the link points at a concrete resource payload.
    /// </summary>
    public Type? ResourceType { get; }

    /// <summary>
    /// Gets or sets the relation name that should be exposed to clients.
    /// </summary>
    public string? Rel { get; set; }
}
