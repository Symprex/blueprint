using System.Net;
using System.Net.Http.Headers;
using Blueprint.Http;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves <c>AddBlueprintClient</c> registers a generated typed client with no reflection over the operations: the client is
/// made by its generated <c>Create</c>, which resolves each readonly field the hand-written class declares from the container,
/// over an <see cref="IHttpClientFactory"/> client that the returned builder configures.
/// </summary>
public sealed class BlueprintClientRegistrationTests
{
    [Fact]
    public void A_client_declaring_a_serializer_context_shares_its_options_between_resolves()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<IPricingApi, PricingApi>(static (_, client) => client.BaseAddress = _baseAddress);

        using var provider = services.BuildServiceProvider();

        var first = TransportOf(provider.GetRequiredService<IPricingApi>());
        var second = TransportOf(provider.GetRequiredService<IPricingApi>());

        // The plan a client resolves is cached by its options, so a transient resolve that derived a copy each time would
        // rebuild it each time.
        second.JsonOptions.Should().BeSameAs(first.JsonOptions);
    }

    [Fact]
    public async Task A_client_named_Create_is_made_by_the_registration()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<ICreate, Create>(static (_, client) => client.BaseAddress = _baseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => RespondingWith([], "[]"));

        await using var provider = services.BuildServiceProvider();

        (await provider.GetRequiredService<ICreate>().FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" })).Should().BeEmpty();
    }

    private static BlueprintHttpClient TransportOf(object client)
    {
        var field = client.GetType().GetField("_client", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return (BlueprintHttpClient)field!.GetValue(client)!;
    }

    private static readonly Uri _baseAddress = new("https://default.example.test/");

    [Fact]
    public async Task A_field_of_the_client_is_resolved_from_the_container_and_the_handler_sees_the_request()
    {
        var sent = new List<Uri>();
        var endpoints = new RegionEndpoints();
        endpoints.Regions["eu"] = new Uri("https://eu.example.test/");

        var services = new ServiceCollection();
        services.AddSingleton(endpoints);
        services.AddBlueprintClient<IRegionalOperationsApi, RegionalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => RespondingWith(sent, "[]"));

        await using var provider = services.BuildServiceProvider();

        // ForRegion reads the endpoints field, so it only reaches the region where the container's instance was injected.
        await provider.GetRequiredService<IRegionalOperationsApi>()
            .ForRegion("eu")
            .FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Should().ContainSingle().Which.Host.Should().Be("eu.example.test");
    }

    [Fact]
    public async Task The_callback_configures_the_http_client_the_requests_are_sent_with()
    {
        var sent = new List<Uri>();
        var services = new ServiceCollection();
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => RespondingWith(sent, "[]"));

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ILocalOperationsApi>().FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Should().ContainSingle().Which.Host.Should().Be("default.example.test");
    }

    [Fact]
    public async Task The_callback_receives_the_service_provider_the_client_is_resolved_from()
    {
        var sent = new List<Uri>();
        var services = new ServiceCollection();
        services.AddSingleton(new RegionEndpoints { Regions = { ["eu"] = new Uri("https://eu.example.test/") } });
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(
                static (sp, client) => client.BaseAddress = sp.GetRequiredService<RegionEndpoints>().Regions["eu"])
            .ConfigurePrimaryHttpMessageHandler(() => RespondingWith(sent, "[]"));

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ILocalOperationsApi>().FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        sent.Should().ContainSingle().Which.Host.Should().Be("eu.example.test");
    }

    [Fact]
    public async Task A_field_the_container_cannot_supply_fails_the_resolve_naming_it()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<IRegionalOperationsApi, RegionalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IRegionalOperationsApi>();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(RegionEndpoints)}*");
    }

    [Fact]
    public async Task A_nullable_field_the_container_cannot_supply_is_left_null()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<IOptionalDependencyApi, OptionalDependencyApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        ((OptionalDependencyApi)provider.GetRequiredService<IOptionalDependencyApi>()).HasEndpoints.Should().BeFalse();
    }

    [Fact]
    public async Task A_nullable_field_the_container_can_supply_is_given_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RegionEndpoints());
        services.AddBlueprintClient<IOptionalDependencyApi, OptionalDependencyApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        ((OptionalDependencyApi)provider.GetRequiredService<IOptionalDependencyApi>()).HasEndpoints.Should().BeTrue();
    }

    [Fact]
    public async Task A_nullable_value_type_field_the_container_cannot_supply_is_left_null()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<IOptionalDependencyApi, OptionalDependencyApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        ((OptionalDependencyApi)provider.GetRequiredService<IOptionalDependencyApi>()).Limit.Should().BeNull();
    }

    [Fact]
    public async Task A_nullable_value_type_field_the_container_can_supply_is_given_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(int), 42);
        services.AddBlueprintClient<IOptionalDependencyApi, OptionalDependencyApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        ((OptionalDependencyApi)provider.GetRequiredService<IOptionalDependencyApi>()).Limit.Should().Be(42);
    }

    [Fact]
    public async Task A_message_handler_chained_on_the_returned_builder_sees_the_request()
    {
        var seen = new List<Uri>();
        var sent = new List<Uri>();
        var services = new ServiceCollection();
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress)
            .AddHttpMessageHandler(() => new RecordingHandler(seen))
            .ConfigurePrimaryHttpMessageHandler(() => RespondingWith(sent, "[]"));

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ILocalOperationsApi>().FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        seen.Should().ContainSingle().Which.Host.Should().Be("default.example.test");
        sent.Should().ContainSingle("the chained handler passes the request on to the primary handler");
    }

    [Fact]
    public async Task The_client_is_transient()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILocalOperationsApi>().Should().NotBeSameAs(provider.GetRequiredService<ILocalOperationsApi>());
    }

    [Fact]
    public async Task A_second_registration_registers_the_interface_once_and_runs_both_callbacks_in_order()
    {
        var sent = new List<Uri>();
        var services = new ServiceCollection();
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) =>
            {
                client.BaseAddress = _baseAddress;
                client.DefaultRequestHeaders.Add("X-First", "1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => RespondingWith(sent, "[]"));
        var second = services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) => client.BaseAddress = new Uri("https://second.example.test/"));

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ILocalOperationsApi>().FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        services.Count(static s => s.ServiceType == typeof(ILocalOperationsApi)).Should().Be(1);
        second.Name.Should().Be(nameof(LocalOperationsApi), "the second call returns the builder of the same named client");

        // Both callbacks configure the one named client, in registration order, so the second's base address wins and the
        // first's header still applies.
        sent.Should().ContainSingle().Which.Host.Should().Be("second.example.test");
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LocalOperationsApi))
            .DefaultRequestHeaders.GetValues("X-First").Should().Equal("1");
    }

    [Fact]
    public async Task An_interface_registered_beforehand_still_leaves_the_named_client_configured_for_a_forward()
    {
        // Registering the interface first (a test double, say) used to skip the named client's configuration, so a
        // gateway's generated forward, which takes the named client by name, had no base address (#164).
        var registeredFirst = new LocalOperationsApi(new BlueprintHttpClient(new System.Net.Http.HttpClient()));
        var services = new ServiceCollection();
        services.AddSingleton<ILocalOperationsApi>(registeredFirst);
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LocalOperationsApi)).BaseAddress.Should().Be(_baseAddress);
        BlueprintClientTransport.Create<LocalOperationsApi>(provider).HttpClient.BaseAddress.Should().Be(_baseAddress);
        provider.GetRequiredService<ILocalOperationsApi>().Should().BeSameAs(registeredFirst, "the interface registered first stands");
        services.Count(static s => s.ServiceType == typeof(ILocalOperationsApi)).Should().Be(1);
    }

    [Fact]
    public async Task The_clients_share_one_set_of_json_options_so_the_plan_is_made_once()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<ILocalOperationsApi, LocalOperationsApi>(static (_, client) => client.BaseAddress = _baseAddress);

        await using var provider = services.BuildServiceProvider();

        var first = ((LocalOperationsApi)provider.GetRequiredService<ILocalOperationsApi>()).Transport.JsonOptions;
        var second = ((LocalOperationsApi)provider.GetRequiredService<ILocalOperationsApi>()).Transport.JsonOptions;

        first.Should().BeSameAs(second);
        first.Should().BeSameAs(provider.GetRequiredService<IOptions<BlueprintJsonOptions>>().Value.SerializerOptions);
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly List<Uri> _seen;

        public RecordingHandler(List<Uri> seen)
        {
            this._seen = seen;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this._seen.Add(request.RequestUri!);

            return base.SendAsync(request, cancellationToken);
        }
    }

    private static FakeHttpHandler RespondingWith(List<Uri> sent, string json)
    {
        return new FakeHttpHandler(request =>
        {
            sent.Add(request.RequestUri!);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, new MediaTypeHeaderValue("application/json")) };
        });
    }
}

/// <summary>
/// A typed client whose only dependency is nullable, which a registration leaves null where the container has none.
/// </summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(FindTemplateCopiesQuery))]
internal partial class OptionalDependencyApi
{
    private readonly RegionEndpoints? _endpoints;

    private readonly int? _limit;

    /// <summary>
    /// Gets a value indicating whether the container supplied the endpoints.
    /// </summary>
    public bool HasEndpoints => this._endpoints is not null;

    /// <summary>
    /// Gets the nullable value-type dependency the container supplied, or <see langword="null"/>.
    /// </summary>
    public int? Limit => this._limit;
}

/// <summary>
/// A typed client named as the factory the registration makes clients with.
/// </summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(FindTemplateCopiesQuery))]
internal partial class Create;
