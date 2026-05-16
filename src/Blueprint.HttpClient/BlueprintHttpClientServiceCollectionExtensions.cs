using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Microsoft.Extensions.DependencyInjection;

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

    // ────────────────────────────────────────────────────────────────────────────────
    //  S365 migration compat: AddBlueprintClient<TInterface, TImplementation>
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a typed HTTP client implementation against its interface using an
    /// <see cref="IHttpClientFactory"/>-backed named client. The implementation receives a
    /// <see cref="BlueprintHttpClient"/> (the non-generic compat base class) as a constructor
    /// parameter, populated from a runtime-scanned <see cref="BlueprintApp"/> built by reflecting
    /// over <see cref="IOperation"/> types and their link/HTTP-method attributes in the contract
    /// assembly.
    /// <para>
    /// This overload matches the Signature 365 registration pattern:
    /// <c>services.AddBlueprintClient&lt;IPortalApi, PortalApi&gt;((s, c) =&gt; { c.BaseAddress = ...; });</c>
    /// </para>
    /// </summary>
    /// <typeparam name="TInterface">The service interface (e.g. <c>IPortalApi</c>).</typeparam>
    /// <typeparam name="TImplementation">The implementation class generated by the S365 source generator.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureClient">
    /// Callback to set <see cref="System.Net.Http.HttpClient.BaseAddress"/>,
    /// default headers, authentication, etc. Receives the scoped <see cref="IServiceProvider"/>
    /// for gateway JWT forwarding scenarios.
    /// </param>
    /// <param name="contractsAssembly">
    /// The assembly that contains the contract operation types. Defaults to the assembly containing
    /// <typeparamref name="TInterface"/> when <c>null</c>.
    /// </param>
    public static void AddBlueprintClient<TInterface,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services,
        Action<IServiceProvider, System.Net.Http.HttpClient> configureClient,
        Assembly? contractsAssembly = null)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        // Idempotent: skip if already registered (mirrors S365 behavior)
        if (services.Any(static s => s.ServiceType == typeof(TInterface)))
        {
            return;
        }

        var assembly = contractsAssembly ?? typeof(TInterface).Assembly;
        var app = RuntimeScanBlueprintApp.ScanAssembly(assembly);
        var httpClientName = typeof(TImplementation).Name;
        var jsonOptions = BlueprintJsonOptions.CreateDefaultOptions();

        var builder = services.AddHttpClient(httpClientName, (sp, client) =>
        {
            configureClient(sp, client);
        });

#if DEBUG
        builder.ConfigureHttpClient(static (_, c) =>
        {
            c.Timeout = Timeout.InfiniteTimeSpan;
        });
#endif

        services.AddTransient<TInterface, TImplementation>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(httpClientName);
            var blueprintHttpClient = new BlueprintHttpClient<RuntimeScanBlueprintApp>(httpClient, app, jsonOptions);

            return ActivatorUtilities.CreateInstance<TImplementation>(sp, (BlueprintHttpClient)blueprintHttpClient);
        });
    }
}
