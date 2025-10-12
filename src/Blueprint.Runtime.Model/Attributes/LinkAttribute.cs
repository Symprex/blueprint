namespace Blueprint.Model.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class LinkAttribute : Attribute
{
    public LinkAttribute(Type? resourceType, string routePattern, string? rel = null)
    {
        this.ResourceType = resourceType;
        this.RoutePattern = routePattern;
        this.Rel = rel;
    }

    // protected LinkAttribute()
    // {
    // }

    public string RoutePattern { get; }

    public Type? ResourceType { get; }

    public string? Rel { get; set; }
}
