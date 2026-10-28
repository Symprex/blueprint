using Blueprint.Model;

namespace Blueprint.Authorization;

/// <summary>
/// What an <see cref="IResourceAuthorizationProvider"/> is asked to decide: the operation's resolved scope pairs, why it
/// is being asked, and, for a link, the resource the link would be written on.
/// </summary>
/// <remarks>
/// A struct with factory methods so the happy path allocates nothing and a request can only be made in a valid shape: a
/// <see cref="ResourceAuthorizationMode.Link"/> request always carries its resource, and no other mode does.
/// </remarks>
public readonly struct ResourceAuthorizationRequest
{
    private readonly IReadOnlyList<ResolvedScopePair>? _scopePairs;

    private ResourceAuthorizationRequest(ResourceAuthorizationMode mode, IReadOnlyList<ResolvedScopePair> scopePairs, object? resource)
    {
        this.Mode = mode;
        this._scopePairs = scopePairs;
        this.Resource = resource;
    }

    /// <summary>
    /// Gets why the provider is being asked.
    /// </summary>
    public ResourceAuthorizationMode Mode { get; }

    /// <summary>
    /// Gets the scope pairs declared on the operation, with their resource keys resolved for <see cref="Mode"/>. Any-of
    /// semantics: satisfying one pair satisfies the request. Never <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<ResolvedScopePair> ScopePairs => this._scopePairs ?? [];

    /// <summary>
    /// Gets the resource a link would be written on, for <see cref="ResourceAuthorizationMode.Link"/>; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public object? Resource { get; }

    /// <summary>
    /// Creates a request to authorise the operation's execution.
    /// </summary>
    /// <param name="scopePairs">The operation's scope pairs, resolved against the operation.</param>
    /// <returns>The request.</returns>
    public static ResourceAuthorizationRequest Execute(IReadOnlyList<ResolvedScopePair> scopePairs)
    {
        ArgumentNullException.ThrowIfNull(scopePairs);

        return new ResourceAuthorizationRequest(ResourceAuthorizationMode.Execute, scopePairs, resource: null);
    }

    /// <summary>
    /// Creates a request to decide whether a link to the operation may be written on <paramref name="resource"/>.
    /// </summary>
    /// <param name="scopePairs">The operation's scope pairs, resolved against <paramref name="resource"/>.</param>
    /// <param name="resource">The resource the link would be written on.</param>
    /// <returns>The request.</returns>
    public static ResourceAuthorizationRequest Link(IReadOnlyList<ResolvedScopePair> scopePairs, object resource)
    {
        ArgumentNullException.ThrowIfNull(scopePairs);
        ArgumentNullException.ThrowIfNull(resource);

        return new ResourceAuthorizationRequest(ResourceAuthorizationMode.Link, scopePairs, resource);
    }

    /// <summary>
    /// Creates a request to decide whether the operation may be listed for the caller before any resource exists.
    /// </summary>
    /// <param name="scopePairs">The operation's scope pairs, with only the caller's own tokens resolved.</param>
    /// <returns>The request.</returns>
    public static ResourceAuthorizationRequest Listing(IReadOnlyList<ResolvedScopePair> scopePairs)
    {
        ArgumentNullException.ThrowIfNull(scopePairs);

        return new ResourceAuthorizationRequest(ResourceAuthorizationMode.Listing, scopePairs, resource: null);
    }
}
