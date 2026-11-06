using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blueprint.HttpClient;

/// <summary>
/// Extension methods for registering <see cref="BlueprintHttpClient{TApp}"/> with the DI container.
/// </summary>
public static class BlueprintHttpClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="BlueprintHttpClient{TApp}"/> as a typed HTTP client using
    /// <see cref="IHttpClientFactory"/>. The caller provides a <paramref name="configureClient"/>
    /// callback to set <see cref="System.Net.Http.HttpClient.BaseAddress"/> and any default headers.
    /// </summary>
    /// <typeparam name="TApp">The <see cref="BlueprintApp"/> that owns the operation descriptors.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">
    /// A callback to configure the <see cref="System.Net.Http.HttpClient"/>, typically to set the base address.
    /// </param>
    /// <param name="jsonOptions">Optional custom JSON serializer options. Defaults to Blueprint's standard config.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration (e.g., adding Polly policies).</returns>
    public static IHttpClientBuilder AddBlueprintHttpClient<TApp>(
        this IServiceCollection services,
        Action<System.Net.Http.HttpClient> configureClient,
        JsonSerializerOptions? jsonOptions = null)
        where TApp : BlueprintApp
    {
        var effectiveOptions = jsonOptions ?? BlueprintJsonOptions.CreateDefaultOptions();

        return services.AddHttpClient<BlueprintHttpClient<TApp>>((sp, client) =>
        {
            configureClient(client);
        })
        .AddTypedClient((httpClient, sp) =>
        {
            var app = sp.GetRequiredService<TApp>();
            return new BlueprintHttpClient<TApp>(httpClient, app, effectiveOptions);
        });
    }

    /// <summary>
    /// Registers a <see cref="BlueprintHttpClient{TApp}"/> with a fixed base address.
    /// This is a convenience overload for the common case of a single remote API host.
    /// </summary>
    /// <typeparam name="TApp">The <see cref="BlueprintApp"/> that owns the operation descriptors.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="baseAddress">The base address of the remote Blueprint API.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration.</returns>
    public static IHttpClientBuilder AddBlueprintHttpClient<TApp>(
        this IServiceCollection services,
        Uri baseAddress)
        where TApp : BlueprintApp
    {
        return services.AddBlueprintHttpClient<TApp>(client => client.BaseAddress = baseAddress);
    }

    /// <summary>
    /// Registers a <see cref="BlueprintHttpClient{TApp}"/> using a <paramref name="configureClient"/>
    /// callback that receives both the <see cref="IServiceProvider"/> and <see cref="System.Net.Http.HttpClient"/>.
    /// Use this overload when the client needs access to scoped services such as
    /// <c>IHttpContextAccessor</c> for JWT token forwarding in gateway scenarios.
    /// </summary>
    /// <typeparam name="TApp">The <see cref="BlueprintApp"/> that owns the operation descriptors.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">
    /// A callback receiving the scoped <see cref="IServiceProvider"/> and the
    /// <see cref="System.Net.Http.HttpClient"/> to configure. Typically used to forward the
    /// incoming JWT token via <c>IHttpContextAccessor.HttpContext.Request.Headers.Authorization</c>.
    /// </param>
    /// <param name="jsonOptions">Optional custom JSON serializer options. Defaults to Blueprint's standard config.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for further configuration (e.g., adding Polly policies).</returns>
    public static IHttpClientBuilder AddBlueprintHttpClient<TApp>(
        this IServiceCollection services,
        Action<IServiceProvider, System.Net.Http.HttpClient> configureClient,
        JsonSerializerOptions? jsonOptions = null)
        where TApp : BlueprintApp
    {
        var effectiveOptions = jsonOptions ?? BlueprintJsonOptions.CreateDefaultOptions();

        return services.AddHttpClient<BlueprintHttpClient<TApp>>((sp, client) =>
        {
            configureClient(sp, client);
        })
        .AddTypedClient((httpClient, sp) =>
        {
            var app = sp.GetRequiredService<TApp>();
            return new BlueprintHttpClient<TApp>(httpClient, app, effectiveOptions);
        });
    }

    /// <summary>
    /// Registers a generated typed client against its interface, over an <see cref="IHttpClientFactory"/> named client, so
    /// that handlers such as retry still apply. Each resolve makes a client by its generated
    /// <see cref="IBlueprintClient{TSelf}.Create"/>, which resolves the readonly fields the class declares from the container;
    /// nothing is looked up by reflection.
    /// <para>
    /// This matches the Signature 365 registration pattern:
    /// <c>services.AddBlueprintClient&lt;IPortalApi, PortalApi&gt;((s, c) =&gt; { c.BaseAddress = ...; });</c>
    /// </para>
    /// </summary>
    /// <remarks>
    /// The client reads and writes with the <see cref="BlueprintJsonOptions.SerializerOptions"/> of the container's
    /// <see cref="IOptions{TOptions}"/>, one instance for every client, so the plan a client resolves from its JSON options is
    /// made once. Those options are made read-only when the first client is resolved, so configure them (through
    /// <see cref="IOptions{TOptions}"/>) before then. Where reflection-based serialization is disabled and they have no
    /// resolver, registration gives them an empty one before freezing them, so a client's <c>SerializerContext</c> supplies
    /// the metadata. It is transient: it holds nothing a call changes, and the <see cref="System.Net.Http.HttpClient"/> it is
    /// given is the factory's for each resolve.
    /// </remarks>
    /// <typeparam name="TInterface">The service interface (e.g. <c>IPortalApi</c>).</typeparam>
    /// <typeparam name="TImplementation">The generated typed client class (e.g. <c>PortalApi</c>).</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">
    /// Callback to set <see cref="System.Net.Http.HttpClient.BaseAddress"/>,
    /// default headers, authentication, etc. Receives the <see cref="IServiceProvider"/>
    /// for gateway JWT forwarding scenarios.
    /// </param>
    /// <returns>
    /// An <see cref="IHttpClientBuilder"/> for the named client (named for <typeparamref name="TImplementation"/>), for further
    /// configuration such as a primary handler or retry. Where <typeparamref name="TInterface"/> is already registered nothing
    /// is added and the builder for the named client <c>typeof(TImplementation).Name</c> is returned. That configures the
    /// existing client only if its registration was made by this method for the same implementation; a manual registration,
    /// or one against another implementation, leaves the returned builder configuring a client nothing uses.
    /// </returns>
    public static IHttpClientBuilder AddBlueprintClient<TInterface, TImplementation>(
        this IServiceCollection services,
        Action<IServiceProvider, System.Net.Http.HttpClient> configureClient)
        where TInterface : class
        where TImplementation : class, TInterface, IBlueprintClient<TImplementation>
    {
        var httpClientName = typeof(TImplementation).Name;

        // Idempotent: skip if already registered (mirrors S365 behavior)
        if (services.Any(static s => s.ServiceType == typeof(TInterface)))
        {
            return services.AddHttpClient(httpClientName);
        }

        var builder = services.AddHttpClient(httpClientName, (sp, client) =>
        {
            configureClient(sp, client);
        });

        services.AddTransient<TInterface>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);

            // The one Blueprint JSON options instance, read-only, which a gateway's generated forward through this client
            // uses too (BlueprintClientTransport).
            var jsonOptions = BlueprintClientTransport.GetJsonOptions(sp);

            return TImplementation.Create(sp, new BlueprintHttpClient(httpClient, jsonOptions));
        });

        return builder;
    }
}
