using System.Net;
using System.Text;
using Blueprint.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;
using NodaTime;
using Microsoft.Extensions.Options;
using NodaTime.Serialization.SystemTextJson;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves that a client registered with <c>AddBlueprintClient</c> serialises through the app's configured
/// <see cref="BlueprintJsonOptions"/>, as legacy Signature365's did.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>origin/master</c>, <c>Common.Core/Api/BlueprintHttpClientRegistration.cs:83</c>) created its
/// <c>BlueprintHttpClient</c> through DI, whose constructor took <c>IOptions&lt;BlueprintJsonOptions&gt;</c> and used its
/// <c>SerializerOptions</c> (<c>BlueprintHttpClient.cs:40-44</c>): the same options the app's own host used, with NodaTime's
/// converters when the app configured them. A service-to-service call whose request or response carries an
/// <see cref="Instant"/> depends on it.
/// </remarks>
public sealed class BlueprintClientJsonOptionsTests
{
    [Fact]
    public async Task A_registered_client_reads_a_NodaTime_response_property_through_the_apps_configured_json_options()
    {
        var services = new ServiceCollection();

        services.AddOptions<BlueprintJsonOptions>()
            .Configure(static options => options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb));

        services.AddBlueprintClient<ITemplateSearchClient, TemplateSearchClient>(
            static (_, client) => client.BaseAddress = new Uri("https://localhost/api/"),
            typeof(SearchTemplatesQuery).Assembly);

        services.AddHttpClient(nameof(TemplateSearchClient))
            .ConfigurePrimaryHttpMessageHandler(static () => new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    [{"id":"t-1","name":"Template 1","createdAt":"2024-01-02T03:04:05Z"}]
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));

        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ITemplateSearchClient>().SearchAsync("template");

        result.Should().ContainSingle().Which.CreatedAt.Should().Be(Instant.FromUtc(2024, 1, 2, 3, 4, 5));
    }

    [Fact]
    public async Task A_client_that_serialises_before_the_app_is_resolved_does_not_stop_the_app_installing_its_serializer_context()
    {
        // Legacy configured BlueprintJsonOptions only through services.Configure. Here the client, which shares those options,
        // reads a response (making them read-only) before anything has asked for the app. It reads plain strings, which is
        // all the app's source-generated context and the options both know.
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>();
        services.AddOptions<BlueprintJsonOptions>()
            .Configure(static options => options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb));
        AddClientRespondingWithAnEmptyList(services);

        await using var provider = services.BuildServiceProvider();

        var names = await provider.GetRequiredService<ITemplateSearchClient>().SearchAsync("template");

        names.Should().BeEmpty();

        var resolveApp = () => provider.GetRequiredService<SampleBlueprintApp>();

        resolveApp.Should().NotThrow("the app's context is installed when the options are made, not when the app is resolved");
        resolveApp.Should().NotThrow("and a failed resolution is not remembered to fail every later one");
        ContextReadsNodaTime(provider.GetRequiredService<SampleBlueprintApp>()).Should().BeTrue();
    }

    [Fact]
    public async Task An_app_resolved_before_anything_uses_the_json_options_has_its_serializer_context_built_on_them()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>();
        services.AddOptions<BlueprintJsonOptions>()
            .Configure(static options => options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb));

        await using var provider = services.BuildServiceProvider();

        ContextReadsNodaTime(provider.GetRequiredService<SampleBlueprintApp>()).Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_default_json_options_get_the_apps_serializer_context_not_a_named_copy_made_first()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>();
        services.AddOptions<BlueprintJsonOptions>()
            .Configure(static options => options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb));

        await using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IOptionsMonitor<BlueprintJsonOptions>>().Get("other");

        ContextReadsNodaTime(provider.GetRequiredService<SampleBlueprintApp>()).Should().BeTrue();
    }

    [Fact]
    public async Task A_scoped_snapshot_of_the_json_options_does_not_replace_the_context_the_app_installed()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>();
        services.AddOptions<BlueprintJsonOptions>();

        await using var provider = services.BuildServiceProvider();

        var installed = provider.GetRequiredService<SampleBlueprintApp>().AppOwnedSerializerContext;

        using var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<BlueprintJsonOptions>>().Value;

        provider.GetRequiredService<SampleBlueprintApp>().AppOwnedSerializerContext.Should().BeSameAs(installed);
    }

    [Fact]
    public async Task A_registered_client_of_an_app_that_configures_no_json_options_reads_a_response_with_blueprints_defaults()
    {
        var services = new ServiceCollection();

        services.AddBlueprintClient<ITemplateSearchClient, TemplateSearchClient>(
            static (_, client) => client.BaseAddress = new Uri("https://localhost/api/"),
            typeof(SearchTemplatesQuery).Assembly);

        services.AddHttpClient(nameof(TemplateSearchClient))
            .ConfigurePrimaryHttpMessageHandler(static () => new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    [{"id":"t-2","name":"Template 2"}]
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));

        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ITemplateSearchClient>().SearchAsync("template");

        result.Should().ContainSingle().Which.Id.Should().Be("t-2");
    }

    private static bool ContextReadsNodaTime(SampleBlueprintApp app)
    {
        return app.AppOwnedSerializerContext!.Options.Converters.Any(static converter => converter.CanConvert(typeof(Instant)));
    }

    private static void AddClientRespondingWithAnEmptyList(IServiceCollection services)
    {
        services.AddBlueprintClient<ITemplateSearchClient, TemplateSearchClient>(
            static (_, client) => client.BaseAddress = new Uri("https://localhost/api/"),
            typeof(SearchTemplatesQuery).Assembly);

        services.AddHttpClient(nameof(TemplateSearchClient))
            .ConfigurePrimaryHttpMessageHandler(static () => new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            }));
    }
}

/// <summary>A typed client over the sample contract, as a consumer writes one.</summary>
public interface ITemplateSearchClient
{
    /// <summary>Searches templates by name.</summary>
    /// <param name="term">The search term.</param>
    /// <returns>The templates found.</returns>
    Task<TemplateApiResource[]> SearchAsync(string term);
}

/// <summary>The implementation <see cref="ITemplateSearchClient"/> is registered against.</summary>
public sealed class TemplateSearchClient : ITemplateSearchClient
{
    private readonly BlueprintHttpClient _client;

    /// <summary>Initialises a new instance of the <see cref="TemplateSearchClient"/> class.</summary>
    /// <param name="client">The client the registration injects.</param>
    public TemplateSearchClient(BlueprintHttpClient client)
    {
        this._client = client;
    }

    /// <inheritdoc />
    public Task<TemplateApiResource[]> SearchAsync(string term)
    {
        return this._client.CallAsync<TemplateApiResource[]>(new SearchTemplatesQuery { Q = term, Page = 1, PageSize = 5 });
    }
}
