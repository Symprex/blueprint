using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blueprint.HttpClient;

/// <summary>
/// Makes the transport a generated typed client is given: a <see cref="BlueprintHttpClient"/> over the client's named
/// <see cref="System.Net.Http.HttpClient"/> and the container's Blueprint JSON options. <c>AddBlueprintClient</c> gives
/// each client it resolves one, and a gateway operation's generated forward (<c>[Expose&lt;TInner&gt;]</c>) takes one
/// for the client that includes its inner operation, so that a forward reaches the upstream exactly as the client does.
/// </summary>
public static class BlueprintClientTransport
{
    private static readonly object _freezeLock = new();

    /// <summary>
    /// Creates the transport of <typeparamref name="TClient"/>: its named client, <c>typeof(TClient).Name</c>, with the
    /// base address, handlers and retry its <c>AddBlueprintClient</c> registration configured, and the serializer context the
    /// client declares (<see cref="IBlueprintClient{TSelf}.ConfigureTransport"/>).
    /// </summary>
    /// <remarks>
    /// The JSON options are the one <see cref="BlueprintJsonOptions.SerializerOptions"/> instance of the container's
    /// <see cref="IOptions{TOptions}"/>, made read-only here if they are not yet, so that what is cached per options is made
    /// once. Where reflection-based serialization is disabled and they have no resolver, they are given an empty one before
    /// being frozen, so a client's serializer context supplies the metadata.
    /// </remarks>
    /// <typeparam name="TClient">The generated typed client class.</typeparam>
    /// <param name="services">The container to resolve the named client and the JSON options from.</param>
    /// <returns>The transport.</returns>
    /// <exception cref="InvalidOperationException">
    /// The named client has no base address, which is what a client with no <c>AddBlueprintClient</c> registration has.
    /// </exception>
    public static BlueprintHttpClient Create<TClient>(IServiceProvider services)
        where TClient : class, IBlueprintClient<TClient>
    {
        var httpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(TClient).Name);
        if (httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                $"The HTTP client '{typeof(TClient).Name}' has no base address. Register {typeof(TClient).Name} with AddBlueprintClient, setting its base address, before forwarding through it.");
        }

        return TClient.ConfigureTransport(new BlueprintHttpClient(httpClient, GetJsonOptions(services)));
    }

    /// <summary>
    /// Gets the container's Blueprint JSON options, read-only.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The one options instance every client shares.</returns>
    internal static JsonSerializerOptions GetJsonOptions(IServiceProvider services)
    {
        // The app's configured options (NodaTime's converters, say), or Blueprint's defaults when it configures none, as
        // legacy's client took them from DI (Signature365 origin/master, Common.Core/Api/BlueprintHttpClient.cs:40-44).
        // IOptions gives the one instance each time, and the transport shares it, so what is cached by options (a typed
        // client's plan) is made once, not for each resolve.
        var jsonOptions = services.GetRequiredService<IOptions<BlueprintJsonOptions>>().Value.SerializerOptions;

        // Read-only before the client is made, as its first call would make them (ResolveTypeInfo): options still open to
        // change are derived afresh by a client declaring a SerializerContext, for each resolve, so each would rebuild its plan.
        if (!jsonOptions.IsReadOnly)
        {
            FreezeOptions(jsonOptions);
        }

        return jsonOptions;
    }

    private static void FreezeOptions(JsonSerializerOptions jsonOptions)
    {
        // Together, so that resolves racing to be first do not set a resolver on options another has already made read-only.
        lock (_freezeLock)
        {
            if (jsonOptions.IsReadOnly)
            {
                return;
            }

            if (jsonOptions.TypeInfoResolver is null && !JsonSerializer.IsReflectionEnabledByDefault)
            {
                // Reflection is off and nothing supplies metadata, where a missing resolver cannot be filled in and freezing
                // would throw before the client's own context is applied. An empty one holds the place: a type that no
                // context covers then fails for want of metadata, rather than the whole client failing to resolve.
                jsonOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine();
            }

            jsonOptions.MakeReadOnly(populateMissingResolver: true);
        }
    }
}
