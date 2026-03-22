using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Model;

/// <summary>
/// A descriptor of an operation, containing details such as the URL from
/// which the operation can be executed, and the type that represents the actual operation.
/// </summary>
public class ApiOperationDescriptor
{
    private readonly Dictionary<Type, object> _featureData = new();
    private readonly Dictionary<string, IReadOnlyList<Attribute>> _propertyAttributes;

    private readonly List<ApiOperationUrl> _urls = new();
    private readonly List<ApiOperationLink> _links = new();

    // private readonly List<ResponseDescriptor> _responses = new();
    // private readonly List<IOperationExecutorBuilder> _handlers = new();

    /// <summary>
    /// Initialises a new instance of the <see cref="ApiOperationDescriptor" /> class.
    /// </summary>
    /// <param name="operationType">The type of the operation.</param>
    /// <param name="name">The name of the operation. Typically this is the class name of the operation with <c>Query</c> and <c>Command</c> removed.</param>
    /// <param name="source">The source of this operation descriptor. Useful for determining _how_ an operation
    /// has found (i.e. scan vs explicit).</param>
    public ApiOperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)] Type operationType,
        string name,
        string source)
    {
        this.OperationType = operationType;
        this.Source = source;
        this._propertyAttributes = CreatePropertyAttributes(operationType);

        this.Name = name;
    }

    /// <summary>
    /// Gets or sets the name of the operation, which is the class name of the operation with Query and Command removed.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets the type that represents this operation, a type that defines the parameters required (or
    /// optional parameters), plus any other metadata that is used by middleware components to
    /// decide what action to take (for example any declarative permissions in the form of
    /// attributes).
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type OperationType { get; }

    /// <summary>
    /// The source of this operation descriptor. Useful for determining _how_ an operation
    /// has found (i.e. scan vs explicit).
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets all registered <see cref="ApiOperationLink" />s for this descriptor.
    /// </summary>
    public IReadOnlyList<ApiOperationLink> Links => this._links;

    /// <summary>
    /// Gets all registered <see cref="ApiOperationUrl" />s for this descriptor.
    /// </summary>
    public IReadOnlyList<ApiOperationUrl> Urls => this._urls;

    /// <summary>
    /// Adds a link for this descriptor.
    /// </summary>
    /// <param name="apiOperationUrl">The URL to add.</param>
    /// <exception cref="NotImplementedException">If the link has not been created for this descriptor.</exception>
    public void AddUrl(ApiOperationUrl apiOperationUrl)
    {
        if (this._urls.Any(u => u.UrlFormat == apiOperationUrl.UrlFormat))
        {
            throw new InvalidOperationException(
                $"Cannot add a URL with the same URL format as an existing URL. Tried to add {apiOperationUrl.UrlFormat}");
        }

        this._urls.Add(apiOperationUrl);
    }

    /// <summary>
    /// Adds a link for this descriptor.
    /// </summary>
    /// <param name="link">The link to add.</param>
    /// <exception cref="NotImplementedException">If the link has not been created for this descriptor.</exception>
    public void AddLink(ApiOperationLink link)
    {
        if (link.OperationDescriptor != this)
        {
            throw new InvalidOperationException($"The ApiOperationLink MUST have been created for this ApiOperationDescriptor, but was instead created for the description {link.OperationDescriptor}");
        }

        var existing = this._links.Where(l =>
                l.UrlFormat.Equals(link.UrlFormat, StringComparison.CurrentCultureIgnoreCase) &&
                l.OperationDescriptor.Name == link.OperationDescriptor.Name &&
                l.ResourceType == link.ResourceType)
            .ToList();

        if (existing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Could not register {link} from {link.OperationDescriptor.Source} as it conflicts with existing Link registrations:\n\n" +
                string.Join("\n", existing.Select(e => $" {e} sourced from {e.OperationDescriptor.Source}")));
        }

        this._links.Add(link);
    }

    /// <summary>
    /// Gets the feature data of the specified type, which provides a pluggable mechanism to add additional
    /// structured data to <see cref="ApiOperationDescriptor" />s, such as HTTP-related data.
    /// </summary>
    /// <typeparam name="TFeatureData">The feature data type to load.</typeparam>
    /// <returns>The feature data of the specified type.</returns>
    /// <exception cref="InvalidOperationException">If no such feature data exists.</exception>
    public TFeatureData? GetFeatureData<TFeatureData>()
    {
        return this.TryGetFeatureData<TFeatureData>(out var featureData) == false
            ? throw new InvalidOperationException($"Could not find feature data of type {typeof(TFeatureData).FullName} for {this.OperationType}")
            : featureData;
    }

    /// <summary>
    /// Tries to get the feature data of the specified type, which provides a pluggable mechanism to add additional
    /// structured data to <see cref="ApiOperationDescriptor" />s, such as HTTP-related data.
    /// </summary>
    /// <param name="feature">The feature data of the specified type.</param>
    /// <typeparam name="TFeatureData">The feature data type to load.</typeparam>
    /// <returns>Whether the feature data exists.</returns>
    public bool TryGetFeatureData<TFeatureData>([NotNullWhen(true)] out TFeatureData? feature)
    {
        if (this._featureData.TryGetValue(typeof(TFeatureData), out var data))
        {
            feature = (TFeatureData)data;

            return true;
        }

        feature = default;
        return false;
    }

    /// <summary>
    /// Adds the given feature data to this descriptor.
    /// </summary>
    /// <param name="newFeatureData">The feature data to store.</param>
    public void SetFeatureData<TFeatureData>(TFeatureData newFeatureData) where TFeatureData : class
    {
        this._featureData[typeof(TFeatureData)] = newFeatureData;
    }

    /// <summary>
    /// Gets all declared attributes for the requested public property.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The property attributes.</returns>
    public IReadOnlyList<Attribute> GetPropertyAttributes(string propertyName)
    {
        if (this._propertyAttributes.TryGetValue(propertyName, out var propertyAttributes))
        {
            return propertyAttributes;
        }

        throw new InvalidOperationException($"Could not find property '{propertyName}' on operation type {this.OperationType}.");
    }

    private static Dictionary<string, IReadOnlyList<Attribute>> CreatePropertyAttributes(Type operationType)
    {
        return operationType.GetProperties()
            .ToDictionary(
                static property => property.Name,
                static property => (IReadOnlyList<Attribute>)property.GetCustomAttributes(inherit: true).OfType<Attribute>().ToArray(),
                StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return this.Name;
    }

    /// <summary>
    /// Creates a new instance of the operation type this descriptor describes.
    /// </summary>
    /// <returns>A new instance of this operation.</returns>
    /// <exception cref="InvalidOperationException">If the operation type does not have a public parameterless constructor.</exception>
    public object CreateInstance()
    {
        var instance = Activator.CreateInstance(this.OperationType);

        return instance is null ? throw new InvalidOperationException($"Could not create instance of type {this.OperationType}") : instance;
    }
}
