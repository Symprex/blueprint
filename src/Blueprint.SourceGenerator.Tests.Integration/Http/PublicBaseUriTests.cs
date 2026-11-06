using System.Net;
using System.Text;
using System.Text.Json;
using Blueprint.Runtime.Expansion;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that <see cref="BlueprintHttpOptions.PublicBaseUri"/>, when set, is the base every generated absolute URL is
/// made under — a resource's self link, its <c>[Link]</c> links, a resource event's href and the root-metadata document —
/// written as configured, in place of the base URI built from the request, and that unset it changes nothing.
/// </summary>
/// <remarks>
/// The fixtures are the operations <see cref="ResourceLinkWireTests"/>, <see cref="ResourceEventHrefTests"/> and
/// <see cref="RootMetadataDocumentTests"/> already declare. Each host here is mounted at <c>api/</c> under the path base
/// <c>/edge</c>, so a test can tell the configured base apart from anything the request or the mount path would add.
/// </remarks>
public sealed class PublicBaseUriTests
{
    private const string PublicBaseUri = "https://api.example.test/v1/";

    [Theory]
    [InlineData("https://api.example.test/v1/")]
    [InlineData("https://api.example.test/v1")]
    [InlineData("https://api.example.test/v1//")]
    public async Task A_resources_self_and_link_links_are_made_under_the_public_base_uri(string configured)
    {
        await using var host = await PublicBaseUriHost.StartAsync(configured);

        using var body = await host.GetJsonAsync("edge/api/link-wire/widgets/widget-1");

        var links = body.RootElement.GetProperty("$links");
        links.GetProperty("self").GetProperty("href").GetString().Should().Be(
            "https://api.example.test/v1/link-wire/widgets/widget-1",
            "the configured base replaces the request's scheme, host, path base and the API's base path, and ends in one slash");
        links.GetProperty("archive").GetProperty("href").GetString().Should().Be("https://api.example.test/v1/link-wire/widgets/widget-1/archive");
    }

    [Fact]
    public async Task A_resource_events_href_is_made_under_the_public_base_uri()
    {
        await using var host = await PublicBaseUriHost.StartAsync(PublicBaseUri);

        using var response = await host.Client.PostAsync("edge/api/href-events/legacy/widgets", new StringContent("{}", Encoding.UTF8, "application/json"));
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        using var body = JsonDocument.Parse(raw);
        body.RootElement.GetProperty("href").GetString().Should().Be("https://api.example.test/v1/href-events/widgets/widget-1");
    }

    [Fact]
    public async Task The_root_metadata_documents_links_are_made_under_the_public_base_uri()
    {
        await using var host = await PublicBaseUriHost.StartAsync(PublicBaseUri);

        using var body = await host.GetJsonAsync("edge/api/");

        body.RootElement.GetProperty("$links").GetProperty("bulk-delete").GetProperty("href").GetString()
            .Should().Be("https://api.example.test/v1/root-metadata/bulk-delete");
    }

    [Fact]
    public async Task The_public_base_uri_is_written_as_configured_whatever_the_public_host()
    {
        // Timestamp's clients compare hrefs to their configured API root by exact string, so the configured base is not
        // round-tripped through Uri (which would, for one, lower-case the host), and PublicHost does not override it.
        await using var host = await PublicBaseUriHost.StartAsync("https://API.Example.test/V1", publicHost: "other.example.test");

        using var body = await host.GetJsonAsync("edge/api/link-wire/widgets/widget-1");

        body.RootElement.GetProperty("$links").GetProperty("self").GetProperty("href").GetString()
            .Should().Be("https://API.Example.test/V1/link-wire/widgets/widget-1");
    }

    [Fact]
    public async Task Without_a_public_base_uri_every_url_is_made_under_the_request_as_before()
    {
        await using var host = await PublicBaseUriHost.StartAsync(publicBaseUri: null);
        var requestBase = host.Client.BaseAddress!.AbsoluteUri + "edge/api/";

        using var widget = await host.GetJsonAsync("edge/api/link-wire/widgets/widget-1");
        widget.RootElement.GetProperty("$links").GetProperty("self").GetProperty("href").GetString().Should().Be(requestBase + "link-wire/widgets/widget-1");
        widget.RootElement.GetProperty("$links").GetProperty("archive").GetProperty("href").GetString().Should().Be(requestBase + "link-wire/widgets/widget-1/archive");

        using var response = await host.Client.PostAsync("edge/api/href-events/legacy/widgets", new StringContent("{}", Encoding.UTF8, "application/json"));
        using var resourceEvent = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        resourceEvent.RootElement.GetProperty("href").GetString().Should().Be(requestBase + "href-events/widgets/widget-1");

        using var root = await host.GetJsonAsync("edge/api/");
        root.RootElement.GetProperty("$links").GetProperty("bulk-delete").GetProperty("href").GetString().Should().Be(requestBase + "root-metadata/bulk-delete");
    }

    [Theory]
    [InlineData("api.example.test/v1/")]
    [InlineData("/v1/")]
    [InlineData("ftp://api.example.test/v1/")]
    [InlineData("https://api.example.test/v1/?tenant=1")]
    [InlineData("https://api.example.test/v1/#links")]
    [InlineData("https://api.example.test/my api/")]
    [InlineData("https://api.example.test\\v1\\")]
    [InlineData(" https://api.example.test/v1/")]
    [InlineData("https://api.example.test/v1/ ")]
    [InlineData("https://api.example.test/v1/\n")]
    [InlineData("https://api.example.test/v1/\t")]
    public async Task A_public_base_uri_that_is_not_an_absolute_http_uri_fails_the_mapping(string configured)
    {
        var act = async () =>
        {
            await using var host = await PublicBaseUriHost.StartAsync(configured);
        };

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(BlueprintHttpOptions.PublicBaseUri)).And.Contain(configured);
    }

    private sealed class PublicBaseUriHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private PublicBaseUriHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<PublicBaseUriHost> StartAsync(string? publicBaseUri, string? publicHost = null)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            // The one expander type every test expanding DepartmentResource uses (see TestDepartmentExpander's remarks).
            builder.Services.AddSingleton<IResourceExpander, TestDepartmentExpander>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint => blueprint
                .AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>()
                .ConfigureHttp(options =>
                {
                    options.PublicBaseUri = publicBaseUri;
                    options.PublicHost = publicHost;
                }));

            var app = builder.Build();

            try
            {
                app.UsePathBase("/edge");
                app.UseRouting();
                app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
                await app.StartAsync();
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new PublicBaseUriHost(app, client);
        }

        public async Task<JsonDocument> GetJsonAsync(string path)
        {
            using var response = await this.Client.GetAsync(path);
            var raw = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was {0}", raw);

            return JsonDocument.Parse(raw);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}
