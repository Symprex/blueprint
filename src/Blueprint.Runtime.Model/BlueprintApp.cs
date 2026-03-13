using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Model;

/// <summary>
/// Provides a data model for an exposed API, a description of the resources that are being exposed, plus
/// the service and entity operations that have been registered.
/// </summary>
public class BlueprintApp
{
    private static readonly List<ApiOperationLink> _emptyLinkList = new(0);

    private readonly Dictionary<Type, ApiOperationDescriptor> _allOperations = new();

    private readonly List<ApiOperationLink> _allLinks = new();

    // Dictionary lookups, contains values from allLinks
    private readonly Dictionary<Type, List<ApiOperationLink>> _operationTypeToLinks = new();
    private readonly Dictionary<Type, List<ApiOperationLink>> _resourceTypeToLinks = new();

    /// <summary>
    /// Gets all registered links for this model, with a link representing the association between a resource and a different
    /// operation (for example a link between the resource 'Jobs' and the operation 'Apply').
    /// </summary>
    public IEnumerable<ApiOperationLink> Links => this._allLinks;

    /// <summary>
    /// Gets all registered operations.
    /// </summary>
    public IEnumerable<ApiOperationDescriptor> Operations => this._allOperations.Values;

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

        foreach (var url in descriptor.Urls)
        {
            var existingDescriptor = this._allOperations.Values
                .FirstOrDefault(d => d.Urls.Any(u => u.UrlFormat == url.UrlFormat));

            if (existingDescriptor != null)
            {
                throw new InvalidOperationException(
                    $"Cannot register URL '{url.UrlFormat}' for operation '{descriptor.Name}' as it is already registered by operation '{existingDescriptor.Name}'.");
            }
        }

        this._allOperations[descriptor.OperationType] = descriptor;

        foreach (var link in descriptor.Links)
        {
            this.RegisterLink(link);
        }
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

        if (!this._resourceTypeToLinks.ContainsKey(link.ResourceType))
        {
            this._resourceTypeToLinks[link.ResourceType] = [];
        }

        var existingLink = this._resourceTypeToLinks[link.ResourceType].SingleOrDefault(l => l.Rel == link.Rel);

        if (existingLink != null)
        {
            throw new InvalidOperationException(
                $"Duplicate link found for resource type {link.ResourceType.Name} with rel {link.Rel}. Existing link is for the operation: \n\n" +
                $" {existingLink.OperationDescriptor.OperationType.FullName}: {existingLink.UrlFormat}\n\n" +
                "Every link must be a unique pairing of resource type (i.e. UserApiResource) and rel (i.e. \"self\" or \"activate\")");
        }

        this._resourceTypeToLinks[link.ResourceType].Add(link);

        if (!this._operationTypeToLinks.ContainsKey(link.OperationDescriptor.OperationType))
        {
            this._operationTypeToLinks[link.OperationDescriptor.OperationType] = [];
        }

        this._operationTypeToLinks[link.OperationDescriptor.OperationType].Add(link);
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
        return this._resourceTypeToLinks.GetValueOrDefault(resourceType, _emptyLinkList);
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
        if (!this._resourceTypeToLinks.TryGetValue(resourceType, out var links))
        {
            return null;
        }

        // foreach (var link in links)
        // {
        //     if (link.Rel == rel)
        //     {
        //         return link;
        //     }
        // }

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
        var found = this.Operations.SingleOrDefault(d => d.OperationType == operationType);

        if (found == null)
        {
            descriptor = null;
            return false;
        }

        descriptor = found;
        return true;
    }
}
