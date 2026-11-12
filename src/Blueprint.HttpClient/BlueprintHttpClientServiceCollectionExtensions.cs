using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

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
    /// configuration such as a primary handler or retry. Where <typeparamref name="TInterface"/> is already registered by
    /// anything else, that registration stands and no other is added, but the named client is still configured, so that a
    /// gateway's generated forward through <typeparamref name="TImplementation"/>, which takes the named client by name, has
    /// its base address.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TImplementation"/> is already registered by this method. Each client is registered once, since a
    /// second registration's <paramref name="configureClient"/> would be applied to the same named client as well.
    /// </exception>
    public static IHttpClientBuilder AddBlueprintClient<TInterface, TImplementation>(
        this IServiceCollection services,
        Action<IServiceProvider, System.Net.Http.HttpClient> configureClient)
        where TInterface : class
        where TImplementation : class, TInterface, IBlueprintClient<TImplementation>
    {
        return services.RegisterBlueprintClient<TInterface, TImplementation>(configureClient, withCallerCredential: false);
    }

    /// <summary>
    /// Registers a generated typed client against its interface as <c>AddBlueprintClient</c> does, resolved already carrying
    /// the <c>Authorization</c> header of the caller the app is serving as its credential: the registration a gateway uses
    /// for a client that calls upstream on the caller's behalf.
    /// <para>
    /// <c>services.AddCallerBlueprintClient&lt;IPortalApi, PortalApi&gt;((s, c) =&gt; c.BaseAddress = ...);</c>
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The header is read once, when the client is resolved, from <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>,
    /// which this registers if nothing has, and given to the client as a <see cref="CallerAuthorizationCredential"/> through
    /// the generated <see cref="IBlueprintClient{TSelf}.WithCredential"/>. Nothing is set on the named
    /// <see cref="System.Net.Http.HttpClient"/>, so concurrent callers never see each other's token; a client resolved outside
    /// a request, or for a caller that sent no <c>Authorization</c> header, sends none.
    /// </para>
    /// <para>
    /// The credential is applied after every other step that shapes a request, so it replaces an <c>Authorization</c> header
    /// the named client's defaults, a call's options or a request modifier set, and a clone the client makes through
    /// <c>WithRequestModifier</c> (a <c>ForRegion</c>, say) keeps it. A later <c>WithCredential</c> on the resolved client
    /// replaces it.
    /// </para>
    /// <para>
    /// Only the credential is forwarded. Any other header of the caller's, such as <c>User-Agent</c>, is the app's to forward,
    /// in <paramref name="configureClient"/> or a request modifier.
    /// </para>
    /// </remarks>
    /// <typeparam name="TInterface">The service interface (e.g. <c>IPortalApi</c>).</typeparam>
    /// <typeparam name="TImplementation">The generated typed client class (e.g. <c>PortalApi</c>).</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">
    /// Callback to set the named client's <see cref="System.Net.Http.HttpClient.BaseAddress"/> and any default headers, as
    /// <c>AddBlueprintClient</c>'s does.
    /// </param>
    /// <returns>
    /// The named client's <see cref="IHttpClientBuilder"/>, as <c>AddBlueprintClient</c> returns it. Where
    /// <typeparamref name="TInterface"/> is already registered by anything else (a test double, say), that registration
    /// stands, credential-free.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TImplementation"/> is already registered by this method or <c>AddBlueprintClient</c>.
    /// </exception>
    public static IHttpClientBuilder AddCallerBlueprintClient<TInterface, TImplementation>(
        this IServiceCollection services,
        Action<IServiceProvider, System.Net.Http.HttpClient> configureClient)
        where TInterface : class
        where TImplementation : class, TInterface, IBlueprintClient<TImplementation>
    {
        return services.RegisterBlueprintClient<TInterface, TImplementation>(configureClient, withCallerCredential: true);
    }

    private static IHttpClientBuilder RegisterBlueprintClient<TInterface, TImplementation>(
        this IServiceCollection services,
        Action<IServiceProvider, System.Net.Http.HttpClient> configureClient,
        bool withCallerCredential)
        where TInterface : class
        where TImplementation : class, TInterface, IBlueprintClient<TImplementation>
    {
        var httpClientName = typeof(TImplementation).Name;

        // One registration per client: a second's callback would run on the same named client too, so a setting both make
        // would be the last one's while a header both append would be sent twice. The named client is configured whoever
        // registered the interface, because a gateway's generated forward (BlueprintClientTransport) takes it by name, not
        // through TInterface.
        if (services.Any(static s => s.ServiceType == typeof(RegisteredBlueprintClient<TImplementation>)))
        {
            throw new InvalidOperationException(
                $"The Blueprint client {typeof(TImplementation).FullName} is already registered by AddBlueprintClient. Register " +
                "each client once: a second registration's configuration would be applied to the same named client as well.");
        }

        services.AddSingleton<RegisteredBlueprintClient<TImplementation>>();

        var builder = services.AddHttpClient(httpClientName, (sp, client) =>
        {
            configureClient(sp, client);
        });

        if (withCallerCredential)
        {
            services.AddHttpContextAccessor();
        }

        // Idempotent for the interface: one registered beforehand (a test double, say) stands.
        if (services.Any(static s => s.ServiceType == typeof(TInterface)))
        {
            return builder;
        }

        services.AddTransient<TInterface>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);

            // The one Blueprint JSON options instance, read-only, which a gateway's generated forward through this client
            // uses too (BlueprintClientTransport).
            var jsonOptions = BlueprintClientTransport.GetJsonOptions(sp);

            var client = TImplementation.Create(sp, new BlueprintHttpClient(httpClient, jsonOptions));

            if (!withCallerCredential)
            {
                return client;
            }

            // Read here, once, so the client carries this caller's header for its whole life, never a later request's.
            var authorization = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();

            return TImplementation.WithCredential(client, new CallerAuthorizationCredential(authorization));
        });

        return builder;
    }

    /// <summary>
    /// Marks, in the service collection, that <c>AddBlueprintClient</c> has registered <typeparamref name="TImplementation"/>,
    /// so that a second call for it is refused.
    /// </summary>
    /// <typeparam name="TImplementation">The generated typed client class.</typeparam>
    private sealed class RegisteredBlueprintClient<TImplementation>;
}
