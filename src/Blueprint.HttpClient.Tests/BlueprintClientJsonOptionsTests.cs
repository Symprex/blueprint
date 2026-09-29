using System.Net;
using System.Text;
using Blueprint.Http;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;
using NodaTime;
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
