using Blueprint.HttpClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Blueprint.Testing;

/// <summary>
/// Registers a <see cref="BlueprintClientStub{TClient}"/> in place of a <c>[BlueprintClient]</c>'s upstream.
/// </summary>
public static class BlueprintClientStubServiceCollectionExtensions
{
    /// <summary>
    /// Answers every request <typeparamref name="TClient"/>'s named <see cref="System.Net.Http.HttpClient"/> sends from a stub
    /// keyed by operation type, instead of the network: a typed-client call and an <c>[Expose&lt;TInner&gt;]</c> gateway's
    /// generated forward alike, as both send through that named client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stub replaces the named client's primary handler, after every other configuration whatever the order of
    /// registration, so no request reaches the network; the delegating handlers the app adds still run. When nothing gives
    /// the named client a base address, as when the app's <c>AddBlueprintClient</c> registration is left out of the test,
    /// it is given <see cref="BlueprintClientStub{TClient}.DefaultBaseAddress"/>.
    /// </para>
    /// <para>
    /// Calling this again for the same client returns the same stub.
    /// </para>
    /// </remarks>
    /// <typeparam name="TClient">The generated typed client class, whose name its named client has.</typeparam>
    /// <param name="services">The container to register the stub in.</param>
    /// <returns>The stub, to give responses to and to read the requests it received from.</returns>
    public static BlueprintClientStub<TClient> StubBlueprintClient<TClient>(this IServiceCollection services)
        where TClient : class, IBlueprintClient<TClient>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.FirstOrDefault(static d => d.ServiceType == typeof(BlueprintClientStub<TClient>))?.ImplementationInstance
            is BlueprintClientStub<TClient> existing)
        {
            return existing;
        }

        var stub = new BlueprintClientStub<TClient>();
        var name = typeof(TClient).Name;

        services.AddSingleton(stub);
        services.AddHttpClient(name);

        // PostConfigure, so the stub's handler and default address are applied after the app's own configuration of the
        // client, whether that was registered before this call or after it.
        services.PostConfigure<HttpClientFactoryOptions>(name, options =>
        {
            options.HttpClientActions.Add(static client => client.BaseAddress ??= BlueprintClientStub<TClient>.DefaultBaseAddress);
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = stub.CreateHandler(builder.Services));
        });

        return stub;
    }
}
