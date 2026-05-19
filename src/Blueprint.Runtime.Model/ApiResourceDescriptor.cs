using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Model;

/// <summary>
/// A descriptor for an API resource type, centralising the metadata about which operations produce
/// or act upon this resource, which links target it, and which state-based visibility checkers
/// apply. The source generator emits resource descriptor registrations automatically by scanning
/// <c>[SelfLink]</c>, <c>[Link]</c>, and <c>[RootLink]</c> attribute declarations.
/// </summary>
/// <remarks>
/// <para>
/// This is the resource-level counterpart to <see cref="ApiOperationDescriptor"/>. Where operation
/// descriptors capture per-operation metadata (URLs, links, feature data), resource descriptors
/// capture per-resource metadata (which operations produce it, all targeting links, state-based
/// checkers that control link visibility).
/// </para>
/// <para>
/// Resource descriptors enable:
/// <list type="bullet">
///   <item>Compile-time validation of resource completeness (e.g., every resource has a self-link)</item>
///   <item>Efficient per-request link population (direct lookup instead of full scan)</item>
///   <item>Developer tooling (resource graph endpoints, OpenAPI enrichment)</item>
///   <item>AOT-friendly static type references (no runtime assembly scanning)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ApiResourceDescriptor
{
    private readonly List<ApiOperationLink> _links = new();
    private readonly List<Type> _stateBasedCheckers = new();
    private readonly List<ApiOperationDescriptor> _producers = new();

    /// <summary>
    /// Initialises a new instance of the <see cref="ApiResourceDescriptor"/> class.
    /// </summary>
    /// <param name="resourceType">The CLR type of the resource.</param>
    /// <param name="name">A stable camelCase name for the resource (e.g., "tenantUser").</param>
    public ApiResourceDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type resourceType,
        string name)
    {
        this.ResourceType = resourceType;
        this.Name = name;
    }

    /// <summary>
    /// Gets the CLR type of the resource this descriptor represents.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type ResourceType { get; }

    /// <summary>
    /// Gets a stable camelCase name for this resource, derived from the type name with
    /// common suffixes removed (e.g., "TenantUserApiResource" → "tenantUser").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the self-link for this resource, or <see langword="null"/> if no self-link has been declared.
    /// A resource with a self-link can be individually addressed by URL.
    /// </summary>
    public ApiOperationLink? SelfLink { get; private set; }

    /// <summary>
    /// Gets all links targeting this resource type (including the self-link if present).
    /// </summary>
    public IReadOnlyList<ApiOperationLink> Links => this._links;

    /// <summary>
    /// Gets the operation descriptors that produce this resource as their result.
    /// An operation "produces" a resource when it declares a <c>[SelfLink]</c> targeting this type.
    /// </summary>
    public IReadOnlyList<ApiOperationDescriptor> Producers => this._producers;

    /// <summary>
    /// Gets the types of <c>StateBasedLinkChecker&lt;TOperation, TResource&gt;</c> implementations
    /// that control link visibility for this resource. Discovered at compile time by the source
    /// generator scanning the app's assemblies.
    /// </summary>
    public IReadOnlyList<Type> StateBasedCheckers => this._stateBasedCheckers;

    /// <summary>
    /// Registers the self-link for this resource. Only the first registration wins;
    /// subsequent calls are silently ignored (the BPLG004 diagnostic handles ambiguity at compile time).
    /// </summary>
    /// <param name="link">The self-link operation link.</param>
    public void SetSelfLink(ApiOperationLink link)
    {
        this.SelfLink ??= link;

        this.AddLink(link);
    }

    /// <summary>
    /// Registers a link targeting this resource.
    /// </summary>
    /// <param name="link">The operation link to register.</param>
    public void AddLink(ApiOperationLink link)
    {
        foreach (var existing in this._links)
        {
            if (existing.Rel == link.Rel && existing.OperationDescriptor == link.OperationDescriptor)
            {
                return;
            }
        }

        this._links.Add(link);
    }

    /// <summary>
    /// Registers an operation that produces this resource as its result.
    /// </summary>
    /// <param name="producer">The operation descriptor that returns this resource type.</param>
    public void AddProducer(ApiOperationDescriptor producer)
    {
        if (!this._producers.Contains(producer))
        {
            this._producers.Add(producer);
        }
    }

    /// <summary>
    /// Registers a state-based link checker type that controls link visibility for this resource.
    /// </summary>
    /// <param name="checkerType">The type implementing <c>StateBasedLinkChecker&lt;,&gt;</c>.</param>
    public void AddStateBasedChecker(Type checkerType)
    {
        if (!this._stateBasedCheckers.Contains(checkerType))
        {
            this._stateBasedCheckers.Add(checkerType);
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Resource:{this.Name} ({this._links.Count} links, {this._producers.Count} producers)";
    }
}
