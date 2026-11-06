using System.Net.Http.Headers;
using System.Text;
using Blueprint.HttpClient;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an operation answering both GET and POST binds each unannotated property from the request it is actually
/// sent: from the query string of a GET and from the body of a POST, through the real router and through a typed client.
/// </summary>
/// <remarks>
/// Legacy Blueprint decided this per request: it read the body of every request but a GET (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Http/MessagePopulation/HttpBodyMessagePopulationSource.cs:145-147</c>) and the query string of a GET alone
/// (<c>BlueprintApiBuilderExtensions.cs:68-73</c>). The generator used to decide it once per operation, so such a property
/// was bound from the body only and a GET lost it, as did the typed client, which sends such an operation as GET
/// (Symprex/blueprint#142).
/// </remarks>
public sealed class MultiMethodBindingWireTests
{
    [Fact]
    public async Task A_GET_binds_every_unannotated_property_from_the_query_string()
    {
        await using var host = await MultiMethodHost.StartAsync();

        var (status, body) = await host.SendAsync(HttpMethod.Get, "multi-method/echo?name=from-query&count=3");

        status.Should().Be(200, body);
        body.Should().Be("\"name=from-query;count=3\"");
    }

    [Fact]
    public async Task A_POST_binds_every_unannotated_property_from_the_body()
    {
        await using var host = await MultiMethodHost.StartAsync();

        var (status, body) = await host.SendAsync(HttpMethod.Post, "multi-method/echo", """{"name":"from-body","count":4}""");

        status.Should().Be(200, body);
        body.Should().Be("\"name=from-body;count=4\"");
    }

    [Fact]
    public async Task A_POST_does_not_bind_from_the_query_string()
    {
        // Legacy read the query string of a GET alone.
        await using var host = await MultiMethodHost.StartAsync();

        var (status, body) = await host.SendAsync(HttpMethod.Post, "multi-method/echo?name=from-query&count=3", "{}");

        status.Should().Be(200, body);
        body.Should().Be("\"name=(unset);count=0\"");
    }

    [Fact]
    public async Task The_typed_client_sends_every_unannotated_property_and_the_handler_sees_them()
    {
        await using var host = await MultiMethodHost.StartAsync();
        using var httpClient = new System.Net.Http.HttpClient { BaseAddress = host.Client.BaseAddress };
        var api = new MultiMethodApi(new BlueprintHttpClient(httpClient));

        var echoed = await api.MultiMethodEchoAsync(new MultiMethodEchoQuery { Name = "from-client", Count = 5 });

        echoed.Should().Be("name=from-client;count=5");
    }

    [Fact]
    public async Task A_GET_through_a_link_without_the_placeholder_binds_it_from_the_query_string()
    {
        // A placeholder in only some links falls back to the request's other source through a link that leaves it out.
        await using var host = await MultiMethodHost.StartAsync();

        var (status, body) = await host.SendAsync(HttpMethod.Get, "multi-method/codes?code=from-query");

        status.Should().Be(200, body);
        body.Should().Be("\"code=from-query\"");
    }

    [Fact]
    public async Task A_POST_through_a_link_without_the_placeholder_binds_it_from_the_body()
    {
        await using var host = await MultiMethodHost.StartAsync();

        var (status, body) = await host.SendAsync(HttpMethod.Post, "multi-method/codes", """{"code":"from-body"}""");

        status.Should().Be(200, body);
        body.Should().Be("\"code=from-body\"");
    }

    [Fact]
    public async Task A_GET_through_a_link_with_the_placeholder_binds_it_from_the_path()
    {
        await using var host = await MultiMethodHost.StartAsync();

        var (status, body) = await host.SendAsync(HttpMethod.Get, "multi-method/codes/from-path?code=from-query");

        status.Should().Be(200, body);
        body.Should().Be("\"code=from-path\"");
    }

    /// <summary>The integration app, registered and mapped as a consumer does, served by Kestrel.</summary>
    private sealed class MultiMethodHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private MultiMethodHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<MultiMethodHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new MultiMethodHost(app, client);
        }

        public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string url, string? json = null)
        {
            using var request = new HttpRequestMessage(method, url);

            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await this.Client.SendAsync(request);

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>A typed client over <see cref="MultiMethodEchoQuery"/>, which it sends as GET, the first of its methods.</summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(MultiMethodEchoQuery))]
internal partial class MultiMethodApi;

/// <summary>An operation answering both GET and POST whose properties are unannotated.</summary>
[AllowAnonymous]
[RootLink("/multi-method/echo")]
[HttpGet]
[HttpPost]
public partial class MultiMethodEchoQuery : IQuery<string>
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public static string Handle(MultiMethodEchoQuery query)
    {
        return $"name={query.Name ?? "(unset)"};count={query.Count}";
    }
}

/// <summary>An operation answering both GET and POST whose placeholder is in only one of its links.</summary>
[AllowAnonymous]
[RootLink("/multi-method/codes/{Code}")]
[RootLink("/multi-method/codes", Rel = "multi-method-codes")]
[HttpGet]
[HttpPost]
public partial class MultiMethodCodeQuery : IQuery<string>
{
    public string? Code { get; set; }

    public static string Handle(MultiMethodCodeQuery query)
    {
        return "code=" + (query.Code ?? "(unset)");
    }
}
