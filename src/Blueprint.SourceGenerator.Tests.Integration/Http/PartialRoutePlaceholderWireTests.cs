using System.Net.Http.Headers;
using System.Text;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a typed route placeholder in only some of an operation's links is bound from the route when present, and
/// falls back to the query string (a GET) or the body (any other method) through a link that leaves it out, through the
/// real router, as legacy Blueprint bound it.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's <c>HttpRouteMessagePopulationSource.GetOwnedProperties</c> (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Http/MessagePopulation/HttpRouteMessagePopulationSource.cs:33-40</c>) owned a placeholder property
/// exclusively only when it was in <em>every</em> of the operation's links — "if it is in ALL links we know we always
/// 'own' the property, otherwise we do not and therefore allow it to be filled by other sources" (:34-35). Its own
/// <c>Build</c> (:60-107) still bound the property from route data whenever a request's route happened to carry it
/// (<c>RouteValues.TryGetValue</c>, falling back to the value already on the operation, :99-107); the query/body source's
/// <c>MessagePopulationMiddlewareBuilder</c> (<c>Blueprint/Middleware/MessagePopulationMiddlewareBuilder.cs:32-59</c>) ran
/// the sources by ascending <c>Priority</c> — the query/body source's is <c>0</c>
/// (<c>HttpPartMessagePopulationSource.cs:72</c>, <c>HttpBodyMessagePopulationSource.cs:27</c>), the route source's
/// <c>100</c> — so a not-fully-route-owned property was read from the query or body first and the route, if present,
/// overwrote it afterwards.
/// </para>
/// <para>
/// Before this fix, the generator computed ownership from the <em>union</em> of every link's placeholders rather than
/// their intersection, so a placeholder-less link left the property at its type's default (<c>0</c>, <c>Guid.Empty</c>,
/// …) with no fallback: R82's "R102" follow-up.
/// </para>
/// </remarks>
public sealed class PartialRoutePlaceholderWireTests
{
    [Fact]
    public async Task A_query_routed_through_its_placeholder_link_binds_the_id_from_the_path()
    {
        await using var host = await PartialPlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("partial-placeholders/queries/routed/42");

        status.Should().Be(200);
        body.Should().Be("\"id=42\"");
    }

    [Fact]
    public async Task The_same_query_routed_through_its_placeholder_less_link_falls_back_to_the_query_string()
    {
        await using var host = await PartialPlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("partial-placeholders/queries/fallback?id=99");

        status.Should().Be(200);
        body.Should().Be("\"id=99\"");
    }

    [Fact]
    public async Task The_same_query_routed_through_its_placeholder_less_link_with_no_query_value_keeps_the_default()
    {
        // The documented v1.0.3 behaviour change for an absent route value (consumer-migration-guide.md), now also true
        // of the query fallback when the request supplies neither.
        await using var host = await PartialPlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("partial-placeholders/queries/fallback");

        status.Should().Be(200);
        body.Should().Be("\"id=0\"");
    }

    [Fact]
    public async Task A_command_routed_through_its_placeholder_link_binds_the_id_from_the_path()
    {
        await using var host = await PartialPlaceholderHost.StartAsync();

        var (status, body) = await host.PostAsync("partial-placeholders/commands/routed/7", "{}");

        status.Should().Be(200);
        body.Should().Be("\"id=7\"");
    }

    [Fact]
    public async Task The_same_command_routed_through_its_placeholder_less_link_falls_back_to_the_body()
    {
        await using var host = await PartialPlaceholderHost.StartAsync();

        var (status, body) = await host.PostAsync("partial-placeholders/commands/fallback", """{"id":13}""");

        status.Should().Be(200);
        body.Should().Be("\"id=13\"");
    }

    [Fact]
    public async Task A_route_value_present_alongside_a_conflicting_query_value_wins_as_legacys_later_priority_did()
    {
        // Both are technically reachable only through the routed link's own URL, so a client cannot really send both —
        // this pins the generated order (query/body before route) rather than a reachable client scenario.
        await using var host = await PartialPlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("partial-placeholders/queries/routed/42?id=1");

        status.Should().Be(200);
        body.Should().Be("\"id=42\"", "the route placeholder is bound after the query fallback and overwrites it");
    }

    /// <summary>The integration app, registered and mapped as a consumer does, served by Kestrel.</summary>
    private sealed class PartialPlaceholderHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private PartialPlaceholderHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<PartialPlaceholderHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<Generators.OperationDescriptors.IntegrationTestBlueprintApp, Generators.OperationDescriptors.IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();
            app.MapBlueprintApi<Generators.OperationDescriptors.IntegrationTestBlueprintApp, Generators.OperationDescriptors.IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new PartialPlaceholderHost(app, client);
        }

        public Task<(int Status, string Body)> GetAsync(string url)
        {
            return this.SendAsync(HttpMethod.Get, url);
        }

        public Task<(int Status, string Body)> PostAsync(string url, string json)
        {
            return this.SendAsync(HttpMethod.Post, url, json);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }

        private async Task<(int Status, string Body)> SendAsync(HttpMethod method, string url, string? json = null)
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
    }
}

/// <summary>A GET with two links: one carrying the <c>Id</c> placeholder, one that leaves it out entirely.</summary>
[AllowAnonymous]
[RootLink("/partial-placeholders/queries/routed/{Id}")]
[RootLink("/partial-placeholders/queries/fallback", Rel = "partial-placeholder-query-fallback")]
public partial class PartialPlaceholderQuery : IQuery<string>
{
    public int Id { get; set; }

    public static string Handle(PartialPlaceholderQuery query)
    {
        return "id=" + query.Id;
    }
}

/// <summary>A command with two links: one carrying the <c>Id</c> placeholder, one that leaves it out entirely.</summary>
[AllowAnonymous]
[RootLink("/partial-placeholders/commands/routed/{Id}")]
[RootLink("/partial-placeholders/commands/fallback", Rel = "partial-placeholder-command-fallback")]
[HttpPost]
public partial class PartialPlaceholderIdCommand : ICommand<string>
{
    public int Id { get; set; }

    public static string Handle(PartialPlaceholderIdCommand command)
    {
        return "id=" + command.Id;
    }
}
