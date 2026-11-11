using System.Net;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// <c>[Expose&lt;TInner&gt;]</c> on real Kestrel (Symprex/blueprint#65): the sample gateway's reads are declared only as
/// <c>[Expose&lt;GetTemplateQuery&gt;]</c> and <c>[Expose&lt;GetTemplatesQuery&gt;]</c> partial classes, with no properties
/// and no handler of their own, and answer with the inner service's resource and the gateway's own links.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayExposeTests(SampleSystemFixture system)
{
    [Fact]
    public async Task The_exposed_op_returns_the_inner_resource_with_gateway_links()
    {
        var id = await this.SeedTemplateAsync("Exposed Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync($"gateway/templates/{id}");

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);

        // The inner resource, not a gateway-shaped copy: the sources are only on the inner TemplateApiResource.
        body.RootElement.GetProperty("id").GetString().Should().Be(id);
        body.RootElement.GetProperty("name").GetString().Should().Be("Exposed Template");
        body.RootElement.GetProperty("htmlSource").GetString().Should().Be("<p>Exposed Template</p>");

        // Links the gateway wrote from its own [SelfLink], never the inner service's.
        var links = body.RootElement.GetProperty("$links");
        links.GetProperty("self").GetProperty("href").GetString()
            .Should().Be(new Uri(system.Gateway.BaseAddress, $"gateway/templates/{id}").ToString());
        links.EnumerateObject().Select(static link => link.Value.GetProperty("href").GetString())
            .Should().OnlyContain(href => href!.StartsWith(system.Gateway.BaseAddress + "gateway/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_exposed_op_answers_the_inner_not_found_result_as_its_own_404()
    {
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates/no-such-exposed-template");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_exposed_get_with_the_inner_route_value_in_its_query_string_reaches_the_inner_path_route()
    {
        // FindGatewayTemplateQuery's route has no {Id}; the inner GetTemplateQuery's does, so the forward sends the
        // query-string value as the inner path segment (#161).
        var id = await this.SeedTemplateAsync("Found By Query Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync($"gateway/templates/find?id={Uri.EscapeDataString(id)}");

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("id").GetString().Should().Be(id);
        body.RootElement.GetProperty("name").GetString().Should().Be("Found By Query Template");
    }

    [Fact]
    public async Task The_exposed_list_returns_the_inner_resources()
    {
        await this.SeedTemplateAsync("Exposed Listed Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates");

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        body.RootElement.EnumerateArray()
            .Should().Contain(static t => t.GetProperty("name").GetString() == "Exposed Listed Template"
                && t.GetProperty("htmlSource").GetString() == "<p>Exposed Listed Template</p>");
    }

    [Fact]
    public async Task An_exposed_command_reads_every_contributed_property_from_a_json_body_the_apps_context_cannot_describe()
    {
        // The gateway binds JSON through GatewayBlueprintAppJsonContext, which is source-generated, and so has no metadata for
        // MergeFields or Recipient: Blueprint emits both into the gateway operation, where STJ's generator cannot see them, and
        // the context lists neither the dictionary nor the nested recipient type.
        var context = Blueprint.SourceGenerator.Sample.Gateway.GatewayBlueprintAppJsonContext.Default;
        context.GetTypeInfo(typeof(Blueprint.SourceGenerator.Sample.Contract.Api.TemplatePreviewRecipient)).Should().BeNull();
        context.GetTypeInfo(typeof(Dictionary<string, string>)).Should().BeNull();

        var id = await this.SeedTemplateAsync("Preview {{FirstName}} {{LastName}} for {{Recipient.Name}} at {{Recipient.Email}}");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());
        using var pipelines = new PipelineRecorder();

        var response = await caller.PostAsync(
            $"gateway/templates/{id}/preview",
            JsonBody("""
                {
                    "mergeFields": { "FirstName": "Ada", "LastName": "Lovelace", "recipient.name": "Eve" },
                    "recipient": { "name": "Charles Babbage", "email": "charles@example.com" }
                }
                """));

        // The recipient wins over a merge field naming it in another case: placeholders match ignoring case, and the merge
        // field used to stay beside the recipient's value and fill {{Recipient.Name}} first.
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("templateId").GetString().Should().Be(id);
        body.RootElement.GetProperty("renderedHtml").GetString()
            .Should().Be("<p>Preview Ada Lovelace for Charles Babbage at charles@example.com</p>");
        body.RootElement.GetProperty("renderedPlainText").GetString()
            .Should().Be("Preview Ada Lovelace for Charles Babbage at charles@example.com");

        // The control for the refusal test below: a forwarded request runs the inner pipeline.
        pipelines.Count("PreviewGatewayTemplateCommandPipeline").Should().Be(1);
        pipelines.Count("PreviewTemplateCommandPipeline").Should().Be(1);
    }

    [Fact]
    public async Task An_exposed_command_whose_json_body_does_not_bind_is_refused_before_it_is_forwarded()
    {
        var id = await this.SeedTemplateAsync("Malformed Preview");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());
        using var pipelines = new PipelineRecorder();

        var response = await caller.PostAsync(
            $"gateway/templates/{id}/preview",
            JsonBody("""{ "recipient": "not an object" }"""));

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("TemplatePreviewRecipient");
        pipelines.Count("PreviewGatewayTemplateCommandPipeline").Should().Be(1);
        pipelines.Count("PreviewTemplateCommandPipeline").Should().Be(0);
    }

    [Fact]
    public void Each_exposed_op_instance_starts_from_its_own_copy_of_a_mutable_inner_default()
    {
        var first = new Blueprint.SourceGenerator.Sample.Gateway.Api.PreviewGatewayTemplateCommand();
        var second = new Blueprint.SourceGenerator.Sample.Gateway.Api.PreviewGatewayTemplateCommand();

        first.MergeFields["FirstName"] = "Ada";

        second.MergeFields.Should().BeEmpty();
        second.MergeFields.Should().NotBeSameAs(first.MergeFields);
    }

    [Fact]
    public async Task An_exposed_ops_OpenAPI_parameters_equal_the_inner_ops()
    {
        using var gateway = system.Gateway.CreateClient(system.CreateCallerToken());
        using var inner = system.Inner.CreateClient(system.CreateCallerToken());

        var exposed = await FindParametersAsync(gateway, "gateway/.blueprint/openapi.json", "/templates/{id}");
        var original = await FindParametersAsync(inner, "api/.blueprint/openapi.json", "/templates/{id}");

        exposed.Should().NotBeEmpty();
        exposed.Should().Equal(original);
    }

    /// <summary>
    /// Records the Blueprint pipelines both in-process apps start while it is open, so a test can tell whether the gateway
    /// forwarded a request to the inner app. Every test that uses the sample system is in one collection, which xUnit runs
    /// serially, so no other test's pipeline starts meanwhile.
    /// </summary>
    private sealed class PipelineRecorder : IDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _started = new();
        private readonly System.Diagnostics.ActivityListener _listener;

        public PipelineRecorder()
        {
            this._listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = static source => source.Name == "Blueprint",
                Sample = static (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                    System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => this._started.Enqueue(activity.OperationName),
            };
            System.Diagnostics.ActivitySource.AddActivityListener(this._listener);
        }

        public int Count(string pipeline)
        {
            return this._started.Count(started => started == pipeline);
        }

        public void Dispose()
        {
            this._listener.Dispose();
        }
    }

    private static StringContent JsonBody(string json)
    {
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    private static async Task<List<string>> FindParametersAsync(System.Net.Http.HttpClient client, string documentPath, string path)
    {
        var response = await client.GetAsync(documentPath);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        using var document = JsonDocument.Parse(text);
        var paths = document.RootElement.GetProperty("paths");
        var names = paths.EnumerateObject().Select(static p => p.Name).ToList();
        var name = names.Should().ContainSingle(n => string.Equals(n, path, StringComparison.OrdinalIgnoreCase), string.Join(", ", names)).Which;
        var operation = paths.GetProperty(name).GetProperty("get");

        return operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(static p => p.GetRawText()).ToList()
            : [];
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
