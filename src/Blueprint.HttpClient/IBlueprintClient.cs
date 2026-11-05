namespace Blueprint.HttpClient;

/// <summary>
/// A typed client the source generator completed, which <c>AddBlueprintClient</c> makes for each resolve with no reflection:
/// the generated <see cref="Create"/> resolves each readonly field the hand-written class declares from the container and
/// passes them to the constructor.
/// </summary>
/// <remarks>
/// Implemented by the generated half of a class marked <see cref="BlueprintClientAttribute"/>; nothing implements it by hand.
/// </remarks>
/// <typeparam name="TSelf">The client class itself.</typeparam>
public interface IBlueprintClient<TSelf>
    where TSelf : IBlueprintClient<TSelf>
{
    /// <summary>
    /// Makes a client over a transport, resolving what its hand-written class declares from the container. A field declared
    /// nullable is left null where the container has none; any other that it cannot supply fails the resolve.
    /// </summary>
    /// <remarks>
    /// A nullable value-type field (<c>int?</c>, <c>TimeSpan?</c>) is resolved as its underlying type, as a container registers
    /// it, and is null where none is registered.
    /// </remarks>
    /// <param name="services">The container the client's dependencies are resolved from.</param>
    /// <param name="client">The transport every call is sent with.</param>
    /// <returns>The new client.</returns>
    static abstract TSelf Create(IServiceProvider services, BlueprintHttpClient client);
}
