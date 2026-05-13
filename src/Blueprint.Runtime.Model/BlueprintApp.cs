using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blueprint.Model;

/// <summary>
/// Provides a data model for an exposed API, a description of the resources that are being exposed, plus
/// the service and entity operations that have been registered.
/// </summary>
public class BlueprintApp
{
    private static readonly IReadOnlyList<ApiOperationLink> _emptyLinkList = [];
    private static readonly IReadOnlyList<ApiOperationDescriptor> _emptyOperationList = [];

    private readonly Dictionary<Type, ApiOperationDescriptor> _allOperations = new();
    private readonly Dictionary<string, List<(ApiOperationDescriptor Descriptor, IReadOnlyList<string> Methods)>> _urlToOperations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ApiOperationDescriptor>> _hostToOperations = new(StringComparer.Ordinal);

    private readonly List<ApiOperationLink> _allLinks = new();

    // Dictionary lookups, contains values from allLinks
    private readonly Dictionary<Type, List<ApiOperationLink>> _operationTypeToLinks = new();
    private readonly Dictionary<Type, List<ApiOperationLink>> _resourceTypeToLinks = new();

    // Frozen read-optimized versions, populated by Freeze()
    private FrozenDictionary<Type, ApiOperationDescriptor>? _frozenOperations;
    private FrozenDictionary<Type, IReadOnlyList<ApiOperationLink>>? _frozenOperationTypeToLinks;
    private FrozenDictionary<Type, IReadOnlyList<ApiOperationLink>>? _frozenResourceTypeToLinks;
    private FrozenDictionary<string, IReadOnlyList<ApiOperationDescriptor>>? _frozenHostToOperations;

    /// <summary>
    /// Gets all registered links for this model, with a link representing the association between a resource and a different
    /// operation (for example a link between the resource 'Jobs' and the operation 'Apply').
    /// </summary>
    public IEnumerable<ApiOperationLink> Links => this._allLinks;

    /// <summary>
    /// Gets all registered operations.
    /// </summary>
    public IEnumerable<ApiOperationDescriptor> Operations =>
        this._frozenOperations is not null
            ? this._frozenOperations.Values
            : this._allOperations.Values;

    /// <summary>
    /// Gets the app-owned serializer context instance that later OpenAPI/schema work can interrogate for generated JSON
    /// metadata without rediscovering serializer ownership from host options.
    /// </summary>
    public JsonSerializerContext? AppOwnedSerializerContext { get; protected set; }

    /// <summary>
    /// Gets the optional app-owned serializer context type that later OpenAPI validation can compare against the
    /// descriptor-published body payload inventory for this Blueprint app.
    /// </summary>
    public Type? AppOwnedSerializerContextType { get; protected set; }

    /// <summary>
    /// Gets the declared body payload CLR types from the app-owned serializer context so runtime schema projection can
    /// resolve serializer metadata without re-parsing compile-time type-name strings.
    /// </summary>
    public ImmutableArray<Type> AppOwnedSerializerContextBodyTypes { get; protected set; } = ImmutableArray<Type>.Empty;

    /// <summary>
    /// Gets the body payload CLR type names declared through the app-owned serializer context so later OpenAPI/schema
    /// slices can reason from stable app metadata instead of rediscovering <c>JsonSerializable</c> attributes.
    /// </summary>
    public ImmutableArray<string> AppOwnedSerializerContextBodyTypeNames { get; protected set; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Registers the given operation descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor to register, must be non-null.</param>
    public void Register(ApiOperationDescriptor descriptor)
    {
        if (this._allOperations.ContainsKey(descriptor.OperationType))
        {
            throw new InvalidOperationException(
                $"Cannot register operation '{descriptor.Name}' as an operation with type '{descriptor.OperationType.Name}' is already registered.");
        }

        var newMethods = GetHttpMethods(descriptor);

        foreach (var url in descriptor.Urls)
        {
            if (this._urlToOperations.TryGetValue(url.UrlFormat, out var existing))
            {
                foreach (var (existingDescriptor, existingMethods) in existing)
                {
                    var overlap = newMethods.Intersect(existingMethods, StringComparer.OrdinalIgnoreCase).ToList();
                    if (overlap.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Cannot register URL '{url.UrlFormat}' for operation '{descriptor.Name}' as HTTP method(s) {string.Join(", ", overlap)} " +
                            $"at that path are already registered by operation '{existingDescriptor.Name}'.");
                    }
                }
            }
        }

        this._allOperations[descriptor.OperationType] = descriptor;
        this.RegisterHostExposure(descriptor);

        foreach (var url in descriptor.Urls)
        {
            if (!this._urlToOperations.TryGetValue(url.UrlFormat, out var entries))
            {
                entries = [];
                this._urlToOperations[url.UrlFormat] = entries;
            }

            entries.Add((descriptor, newMethods));
        }

        foreach (var link in descriptor.Links)
        {
            this.RegisterLink(link);
        }
    }

    private static IReadOnlyList<string> GetHttpMethods(ApiOperationDescriptor descriptor)
    {
        if (descriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methodData))
        {
            return methodData.HttpMethods;
        }

        return ["GET"];
    }

    /// <summary>
    /// Registers the specified link, checking that the link represents a unique combination of HTTP method
    /// and URL format to avoid clashes.
    /// </summary>
    /// <remarks>
    /// In addition the link <see cref="ApiOperationLink.Rel" /> property will be checked to ensure no two links with
    /// the same rel can be associated with a single resource type.
    /// </remarks>
    /// <param name="link">The link to register.</param>
    /// <exception cref="InvalidOperationException">If the link is not unique.</exception>
    private void RegisterLink(ApiOperationLink link)
    {
        this._allLinks.Add(link);

        if (!this._resourceTypeToLinks.TryGetValue(link.ResourceType, out var resourceLinks))
        {
            resourceLinks = [];
            this._resourceTypeToLinks[link.ResourceType] = resourceLinks;
        }

        foreach (var existingLink in resourceLinks)
        {
            if (existingLink.Rel == link.Rel)
            {
                throw new InvalidOperationException(
                    $"Duplicate link found for resource type {link.ResourceType.Name} with rel {link.Rel}. Existing link is for the operation: \n\n" +
                    $" {existingLink.OperationDescriptor.OperationType.FullName}: {existingLink.UrlFormat}\n\n" +
                    "Every link must be a unique pairing of resource type (i.e. UserApiResource) and rel (i.e. \"self\" or \"activate\")");
            }
        }

        resourceLinks.Add(link);

        if (!this._operationTypeToLinks.TryGetValue(link.OperationDescriptor.OperationType, out var operationLinks))
        {
            operationLinks = [];
            this._operationTypeToLinks[link.OperationDescriptor.OperationType] = operationLinks;
        }

        operationLinks.Add(link);
    }

    /// <summary>
    /// Gets all registered links for the given operation type. Multiple links (routes) can be specified for a single
    /// for a single operation as it is possible to access via different URLs depending on link association (i.e.
    /// could be /api/issues and /api/users/3/issues where the second one fills in a UserId property).
    /// </summary>
    /// <param name="operationType">The type of operation to get links for.</param>
    /// <returns>All associated links (routes) for the operation type.</returns>
    /// <exception cref="InvalidOperationException">If no operation of the specified type has been registered.</exception>
    public IEnumerable<ApiOperationLink> GetLinksForOperation(Type operationType)
    {
        if (this._frozenOperations is not null)
        {
            if (!this._frozenOperations.TryGetValue(operationType, out _))
            {
                throw new InvalidOperationException(
                    $"Cannot get links for operation '{operationType.Name}' as it has not been registered.");
            }

            return this._frozenOperationTypeToLinks is not null &&
                   this._frozenOperationTypeToLinks.TryGetValue(operationType, out var frozenLinks)
                ? frozenLinks
                : [];
        }

        return !this._allOperations.TryGetValue(operationType, out var operationDescriptor)
            ? throw new InvalidOperationException(
                $"Cannot get links for operation '{operationType.Name}' as it has not been registered.")
            : (IEnumerable<ApiOperationLink>)(!this._operationTypeToLinks.TryGetValue(operationDescriptor.OperationType, out var operation) ?
            [] :
            operation);
    }

    /// <summary>
    /// Gets all registered links for the given resource type. Links can be associated with a resource so that they get
    /// generated and returned in the $links properties of an <see cref="ILinkableResource"/> instance.
    /// </summary>
    /// <param name="resourceType">The resource type to load links for.</param>
    /// <returns>The (potentially empty) enumeration of registered links.</returns>
    public IReadOnlyList<ApiOperationLink> GetLinksForResource(Type resourceType)
    {
        if (this._frozenResourceTypeToLinks is not null)
        {
            return this._frozenResourceTypeToLinks.GetValueOrDefault(resourceType, _emptyLinkList);
        }

        return this._resourceTypeToLinks.TryGetValue(resourceType, out var links) ? links : _emptyLinkList;
    }

    /// <summary>
    /// Gets all operations that are exposed on the requested host surface.
    /// </summary>
    /// <param name="hostName">The stable host name.</param>
    /// <returns>The registered operations for that host, or an empty list when none are exposed.</returns>
    public IReadOnlyList<ApiOperationDescriptor> GetOperationsForHost(string hostName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);

        if (this._frozenHostToOperations is not null)
        {
            return this._frozenHostToOperations.GetValueOrDefault(hostName, _emptyOperationList);
        }

        return this._hostToOperations.TryGetValue(hostName, out var ops) ? ops : _emptyOperationList;
    }

    /// <summary>
    /// Gets the link that has been registered for the given resource type with the specified 'rel'
    /// property.
    /// </summary>
    /// <param name="resourceType">The resource type to load for.</param>
    /// <param name="rel">The rel of the link to load.</param>
    /// <returns>The associated link, or <c>null</c> if no such link exists.</returns>
    public ApiOperationLink? GetLinkFor(Type resourceType, string rel)
    {
        IReadOnlyList<ApiOperationLink>? links;

        if (this._frozenResourceTypeToLinks is not null)
        {
            if (!this._frozenResourceTypeToLinks.TryGetValue(resourceType, out links))
            {
                return null;
            }
        }
        else if (!this._resourceTypeToLinks.TryGetValue(resourceType, out var mutableLinks))
        {
            return null;
        }
        else
        {
            links = mutableLinks;
        }

        foreach (var link in links)
        {
            if (link.Rel == rel)
            {
                return link;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the <see cref="ApiOperationDescriptor" /> for an operation of the given type, which may be a concrete
    /// implementation of a registered interface-based operation.
    /// </summary>
    /// <param name="operationType">The type of operation to search for.</param>
    /// <returns>The <see cref="ApiOperationDescriptor" />.</returns>
    /// <exception cref="InvalidOperationException">If no operation descriptor exists.</exception>
    public ApiOperationDescriptor FindOperation(Type operationType)
    {
        return !this.TryFindOperation(operationType, out var found)
            ? throw new InvalidOperationException(@$"Could not find an operation of the type {operationType.FullName}.

Make sure that it has been correctly registered through the use of the .Operations(...) configuration method at startup.

Operations will be found polymorphically, meaning an operation could be registered as an interface with the model but found as a concrete implementation type at runtime")
            : found;
    }

    /// <summary>
    /// Finds the <see cref="ApiOperationDescriptor" /> for an operation of the given type, which may be a concrete
    /// implementation of a registered interface-based operation.
    /// </summary>
    /// <param name="operationType">The type of operation to search for.</param>
    /// <param name="descriptor">The <see cref="ApiOperationDescriptor" />, or <c>null</c> if no such operation exists.</param>
    /// <returns>Whether an operation was found.</returns>
    public bool TryFindOperation(Type operationType, [NotNullWhen(true)] out ApiOperationDescriptor? descriptor)
    {
        if (this._frozenOperations is not null)
        {
            return this._frozenOperations.TryGetValue(operationType, out descriptor);
        }

        return this._allOperations.TryGetValue(operationType, out descriptor);
    }

    /// <summary>
    /// Freezes the registration data into read-optimized frozen collections for faster runtime lookup.
    /// Call this after all operations and links have been registered. The generated app registration
    /// code calls this automatically.
    /// </summary>
    public void Freeze()
    {
        this._frozenOperations = this._allOperations.ToFrozenDictionary();

        this._frozenOperationTypeToLinks = this._operationTypeToLinks
            .ToFrozenDictionary(
                static kvp => kvp.Key,
                static kvp => (IReadOnlyList<ApiOperationLink>)kvp.Value);

        this._frozenResourceTypeToLinks = this._resourceTypeToLinks
            .ToFrozenDictionary(
                static kvp => kvp.Key,
                static kvp => (IReadOnlyList<ApiOperationLink>)kvp.Value);

        this._frozenHostToOperations = this._hostToOperations
            .ToFrozenDictionary(
                static kvp => kvp.Key,
                static kvp => (IReadOnlyList<ApiOperationDescriptor>)kvp.Value,
                StringComparer.Ordinal);

        foreach (var descriptor in this._allOperations.Values)
        {
            descriptor.FreezeFeatureData();
        }
    }

    /// <summary>
    /// Replaces the app-owned <see cref="JsonSerializerContext"/> with a new instance constructed
    /// from the given <paramref name="options"/>. This is called by the runtime infrastructure
    /// to wire consumer-configured converters (e.g. NodaTime) into the source-generated serializer
    /// context. The method is safe to call only once, before the app handles any requests.
    /// </summary>
    /// <param name="options">
    /// The <see cref="JsonSerializerOptions"/> to pass to the serializer context constructor.
    /// Must include any custom converters the consumer needs (e.g. NodaTime).
    /// </param>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "The serializer context type is source-generated and always has a (JsonSerializerOptions) constructor. The type reference is kept alive through AppOwnedSerializerContextType.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
        Justification = "The single-argument (JsonSerializerOptions) constructor is always emitted by the System.Text.Json source generator.")]
    public void ApplySerializerOptions(JsonSerializerOptions options)
    {
        if (this.AppOwnedSerializerContextType is not { } contextType)
        {
            return;
        }

        // All JsonSerializerContext subclasses generated by System.Text.Json have a
        // public constructor accepting JsonSerializerOptions. This allows us to rebuild
        // the context with consumer-provided converters (e.g. NodaTime) while keeping
        // all the compile-time source-generated metadata intact.
        this.AppOwnedSerializerContext = (JsonSerializerContext)Activator.CreateInstance(contextType, options)!;
    }

    private void RegisterHostExposure(ApiOperationDescriptor descriptor)
    {
        if (!descriptor.TryGetFeatureData<OperationHostExposureFeatureData>(out var hostExposure))
        {
            return;
        }

        foreach (var hostName in hostExposure.HostNames.Distinct(StringComparer.Ordinal))
        {
            if (!this._hostToOperations.TryGetValue(hostName, out var operations))
            {
                operations = [];
                this._hostToOperations[hostName] = operations;
            }

            operations.Add(descriptor);
        }
    }
}
