namespace Blueprint.Model;

/// <summary>
/// An exception that will be thrown if the format of an <see cref="ApiOperationLink" /> is invalid.
/// </summary>
public class OperationLinkFormatException : Exception
{
    /// <summary>
    /// Initialises a new instance of the <see cref="OperationLinkFormatException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public OperationLinkFormatException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Represents a link between an <see cref="ApiResource" /> and an operation, such that we can render a set of
/// URL's for a given resource that represents operations that can act on that resource.
/// </summary>
/// <seealso cref="ILinkableResource" />
public class ApiOperationLink
{
    private readonly string _description;

    /// <summary>
    /// Initialises a new instance of the <see cref="ApiOperationLink" /> class.
    /// </summary>
    /// <param name="operationDescriptor">The operation this link represents.</param>
    /// <param name="routingUrl">The URL.</param>
    /// <param name="urlFormat">The URL format.</param>
    /// <param name="rel">The relationship this link represents.</param>
    /// <param name="resourceType">The type of the resource this link is for.</param>
    public ApiOperationLink(ApiOperationDescriptor operationDescriptor, string routingUrl, string urlFormat, string rel, Type resourceType)
    {
        this.OperationDescriptor = operationDescriptor;
        this.RoutingUrl = routingUrl;
        this.UrlFormat = urlFormat;
        this.Rel = rel;
        this.ResourceType = resourceType;

        // Let's precalculate this as ApiOperationLinks are expected to stay around for lifetime of the application, so may as well
        // pay this cost upfront.
        this._description = $"{this.ResourceType?.Name ?? "Root"}#{this.Rel} => {this.OperationDescriptor.OperationType.Name}";
    }

    /// <summary>
    /// The descriptor this link points to (i.e. the operation this link is for).
    /// </summary>
    public ApiOperationDescriptor OperationDescriptor { get; }

    /// <summary>
    /// Gets a URL that can be used for routing, trimming the definitions
    /// of placeholders to something simpler (i.e. turns /users/{Id:UserId}/more-details?staticQuery=true to /users/{Id}/more-details).
    /// </summary>
    /// <returns>A URL representation of the link used in routing.</returns>
    public string RoutingUrl { get; }

    /// <summary>
    /// Gets the format of the URL for this link, which will <b>not</b> start with a forward
    /// slash.
    /// </summary>
    public string UrlFormat { get; }

    /// <summary>
    /// The <strong>rel</strong>ationship this link represents, which is the key that will be used
    /// when populating the <see cref="ILinkableResource.Links" /> dictionary.
    /// </summary>
    public string Rel { get; }

    /// <summary>
    /// The resource from which this link can be created, for example a `UserResource` value that
    /// is returned from an operation. This <b>MUST</b> be an <see cref="ILinkableResource" />.
    /// </summary>
    public Type ResourceType { get; } = null!;

    /// <inheritdoc />
    public override string ToString()
    {
        return this._description;
    }
}
