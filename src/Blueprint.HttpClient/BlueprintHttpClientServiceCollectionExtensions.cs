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
}
