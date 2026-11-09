using System.Net;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.Harness;
using Blueprint.EndToEnd.Tests.SampleSystem;
using Blueprint.HttpClient;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// <see cref="BlueprintHttpClient.ForwardAsync{TInner, TResult}(TInner, CallOptions, CancellationToken)"/> against the real inner template service, over a real
/// socket: an upstream 404 becomes the declared <see cref="NotFoundResult{T}"/>, and a forwarded resource arrives without the
/// inner service's <c>$links</c> (D5, D6). The sample gateway's single-template query forwards with it.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayForwardTests(SampleSystemFixture system)
{
    [Fact]
    public async Task An_upstream_404_becomes_the_declared_NotFoundResult()
    {
        using var httpClient = this.CreateInnerHttpClient();
        var upstream = new BlueprintHttpClient(httpClient);

        var result = await upstream.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "no-such-template" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Id.Should().Be("no-such-template");
    }

    [Fact]
    public async Task A_thrown_upstream_not_found_still_escapes_as_the_upstream_problem()
    {
        // The inner handler throws NotFoundException for an unknown id, which the host writes as a problem, not as a
        // NotFoundResult's content, so a forward that declares one still surfaces the problem unchanged.
        using var httpClient = this.CreateInnerHttpClient();
        var upstream = new BlueprintHttpClient(httpClient);

        var act = () => upstream.ForwardAsync<UpdateTemplateCommand, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new UpdateTemplateCommand { Id = "missing", Name = "Renamed", HtmlSource = "<p>Renamed</p>" });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Type.Should().Be("not_found");
        exception.Detail.Should().Be("Template 'missing' not found.");
    }

    [Fact]
    public async Task Against_a_host_negotiating_error_content_types_a_thrown_404_escapes_and_a_NotFoundResult_is_still_read()
    {
        // A host with NegotiateErrorContentType writes an error as application/json to a request with no Accept, so a
        // forward asks for problem JSON; without that, the thrown 404 below would be read as the not-found member.
        await using var host = new MultiAppHost(system.Issuer);
        var inner = await host.StartAsync<Blueprint.SourceGenerator.Sample.Startup>("negotiating-inner", app =>
            app.ConfigureServices = static services => services.Configure<BlueprintHttpOptions>(static http => http.NegotiateErrorContentType = true));
        using var httpClient = inner.CreateClient(system.CreateCallerToken());
        httpClient.BaseAddress = new Uri(inner.BaseAddress, "api/");
        var upstream = new BlueprintHttpClient(httpClient);

        var thrown = () => upstream.ForwardAsync<UpdateTemplateCommand, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new UpdateTemplateCommand { Id = "missing", Name = "Renamed", HtmlSource = "<p>Renamed</p>" });
        var notFound = await upstream.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "no-such-template" });

        (await thrown.Should().ThrowAsync<BlueprintApiException>()).Which.Detail.Should().Be("Template 'missing' not found.");
        notFound.IsT2.Should().BeTrue();
        notFound.AsT2.Content.Id.Should().Be("no-such-template");
    }

    [Fact]
    public async Task A_forwarded_resource_arrives_without_the_inner_links()
    {
        var id = await this.SeedTemplateAsync("Forwarded Links Template");
        using var httpClient = this.CreateInnerHttpClient();

        // The inner service does write links, so their absence below is the forward dropping them.
        using (var raw = JsonDocument.Parse(await httpClient.GetStringAsync($"templates/{id}")))
        {
            raw.RootElement.GetProperty("$links").EnumerateObject().Should().NotBeEmpty();
        }

        var result = await new BlueprintHttpClient(httpClient)
            .ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(new GetTemplateQuery { Id = id });

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Name.Should().Be("Forwarded Links Template");
        result.AsT1.Content.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task The_gateway_forwards_an_unknown_id_as_its_own_404()
    {
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates/no-such-template");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("id").GetString().Should().Be("no-such-template");
    }

    [Fact]
    public async Task The_gateway_answers_a_forwarded_resource_with_its_own_links_only()
    {
        var id = await this.SeedTemplateAsync("Gateway Links Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync($"gateway/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var hrefs = body.RootElement.GetProperty("$links").EnumerateObject()
            .Select(static link => link.Value.GetProperty("href").GetString())
            .ToList();

        hrefs.Should().NotBeEmpty();
        hrefs.Should().OnlyContain(href => href!.StartsWith(system.Gateway.BaseAddress + "gateway/", StringComparison.Ordinal));
    }

    private System.Net.Http.HttpClient CreateInnerHttpClient()
    {
        var httpClient = system.Inner.CreateClient(system.CreateCallerToken());
        httpClient.BaseAddress = new Uri(system.Inner.BaseAddress, "api/");

        return httpClient;
    }

    /// <summary>
    /// Creates a template directly in the inner app, over its own socket, and returns its id.
    /// </summary>
    private async Task<string> SeedTemplateAsync(string name)
    {
        using var inner = system.Inner.CreateClient(system.CreateCallerToken());
        var response = await inner.PostAsJsonAsync(
            "api/templates",
            new { name, htmlSource = $"<p>{name}</p>", plainTextSource = name });
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"seeding should succeed, but the inner app returned {response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }
}
