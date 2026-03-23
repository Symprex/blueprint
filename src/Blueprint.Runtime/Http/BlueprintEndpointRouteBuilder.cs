namespace Blueprint.Http;

/// <summary>
/// Builds conventions that will be used for customization of Blueprint <see cref="EndpointBuilder"/> instances, for
/// example to add hos requirements or other extensions that should apply across all API operations.
/// </summary>
public sealed class BlueprintEndpointRouteBuilder : IEndpointConventionBuilder
{
    private readonly List<IEndpointConventionBuilder> _builders;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlueprintEndpointRouteBuilder"/> class.
    /// </summary>
    /// <param name="builders">The mapped endpoint builders that should receive shared conventions.</param>
    internal BlueprintEndpointRouteBuilder(List<IEndpointConventionBuilder> builders)
    {
        this._builders = builders;
    }

    /// <inheritdoc/>
    public void Add(Action<EndpointBuilder> convention)
    {
        foreach (var b in this._builders)
        {
            b.Add(convention);
        }
    }
}
