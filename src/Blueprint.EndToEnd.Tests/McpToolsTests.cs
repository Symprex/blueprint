using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.EndToEnd.Tests.SampleSystem;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// The sample gateway serves its operations as MCP tools at <c>gateway/mcp</c> (D22). A real MCP client lists and
/// calls them over a real socket, and every call is compared with the same operation called over HTTP.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class McpToolsTests(SampleSystemFixture system)
{
    [Fact]
    public async Task Tools_list_returns_one_tool_per_gateway_operation_named_by_its_operation_id()
    {
        var operationIds = await this.ReadOperationIdsAsync();
        await using var client = await this.ConnectAsync(bearerToken: null);

        var tools = await client.ListToolsAsync();

        tools.Select(static t => t.Name).Should().BeEquivalentTo(operationIds.Values);
        operationIds.Should().HaveCount(3, "the gateway has three operations, each at one URL and method");
    }

    [Fact]
    public async Task A_tool_is_described_by_its_operations_input_schema_and_description()
    {
        var operationIds = await this.ReadOperationIdsAsync();
        await using var client = await this.ConnectAsync(bearerToken: null);

        var tools = await client.ListToolsAsync();

        var getTemplate = tools.Single(t => t.Name == operationIds[("get", "/templates/{id}")]);
        getTemplate.Description.Should().Contain("Retrieve a single template from the upstream template service via the gateway.");

        // The route placeholder is named as the URL declares it, /templates/{Id}.
        var schema = getTemplate.JsonSchema;
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().Equal("Id");
        schema.GetProperty("required").EnumerateArray().Select(static r => r.GetString()).Should().Equal("Id");
    }

    [Fact]
    public async Task An_unauthenticated_tools_call_gets_the_same_401_as_HTTP()
    {
        var operationIds = await this.ReadOperationIdsAsync();
        using var http = system.Gateway.CreateClient();
        var httpResponse = await http.GetAsync("gateway/templates");
        var httpBody = await httpResponse.Content.ReadAsStringAsync();
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, httpBody);
        await using var client = await this.ConnectAsync(bearerToken: null);

        var result = await client.CallToolAsync(operationIds[("get", "/templates")], new Dictionary<string, object?>());

        result.IsError.Should().BeTrue();
        StatusOf(result).Should().Be(401);
        JsonNode.DeepEquals(ParseText(result), JsonNode.Parse(httpBody)).Should().BeTrue(
            $"the tool's error should be the HTTP response's body, {httpBody}, but was {TextOf(result)}");
    }

    [Fact]
    public async Task An_authenticated_tools_call_runs_the_operation_with_the_callers_token_and_returns_what_HTTP_returns()
    {
        var operationIds = await this.ReadOperationIdsAsync();
        var token = system.CreateCallerToken();
        using var http = system.Gateway.CreateClient(token);
        var created = await http.PostAsJsonAsync(
            "gateway/templates",
            new { name = "Fetched By MCP", htmlSource = "<p>mcp</p>", plainTextSource = "mcp" });
        var id = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();
        var httpBody = await http.GetStringAsync($"gateway/templates/{id}");
        await using var client = await this.ConnectAsync(token);

        var result = await client.CallToolAsync(
            operationIds[("get", "/templates/{id}")],
            new Dictionary<string, object?> { ["Id"] = id });

        result.IsError.Should().NotBe(true, TextOf(result));
        StatusOf(result).Should().Be(200);
        JsonNode.DeepEquals(ParseText(result), JsonNode.Parse(httpBody)).Should().BeTrue(
            $"the tool should return the HTTP response's body, {httpBody}, but returned {TextOf(result)}");
        result.StructuredContent!.Value.GetProperty("name").GetString().Should().Be("Fetched By MCP");
    }

    [Fact]
    public async Task A_tools_call_sends_its_body_arguments_as_the_operations_body()
    {
        var operationIds = await this.ReadOperationIdsAsync();
        var token = system.CreateCallerToken();
        await using var client = await this.ConnectAsync(token);

        var result = await client.CallToolAsync(
            operationIds[("post", "/templates")],
            new Dictionary<string, object?>
            {
                ["name"] = "Created By MCP",
                ["htmlSource"] = "<p>created</p>",
                ["plainTextSource"] = "created",
            });

        result.IsError.Should().NotBe(true, TextOf(result));
        var id = result.StructuredContent!.Value.GetProperty("id").GetString();
        using var http = system.Gateway.CreateClient(token);
        var fetched = JsonNode.Parse(await http.GetStringAsync($"gateway/templates/{id}"))!;
        fetched["name"]!.GetValue<string>().Should().Be("Created By MCP");
    }

    [Fact]
    public async Task A_tools_call_that_fails_validation_gets_the_same_422_as_HTTP()
    {
        var operationIds = await this.ReadOperationIdsAsync();
        var token = system.CreateCallerToken();
        var invalid = new Dictionary<string, object?> { ["name"] = string.Empty, ["htmlSource"] = "<p>x</p>", ["plainTextSource"] = "x" };
        using var http = system.Gateway.CreateClient(token);
        var httpResponse = await http.PostAsJsonAsync("gateway/templates", invalid);
        var httpBody = await httpResponse.Content.ReadAsStringAsync();
        httpResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, httpBody);
        await using var client = await this.ConnectAsync(token);

        var result = await client.CallToolAsync(operationIds[("post", "/templates")], invalid);

        result.IsError.Should().BeTrue();
        StatusOf(result).Should().Be(422);
        JsonNode.DeepEquals(ParseText(result), JsonNode.Parse(httpBody)).Should().BeTrue(
            $"the tool's error should be the HTTP response's body, {httpBody}, but was {TextOf(result)}");
    }

    private static string TextOf(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }

    private static JsonNode? ParseText(CallToolResult result)
    {
        return JsonNode.Parse(TextOf(result));
    }

    private static int? StatusOf(CallToolResult result)
    {
        return result.Meta?["httpStatus"]?.GetValue<int>();
    }

    /// <summary>
    /// Reads every operation id from the gateway's own OpenAPI document, keyed by lower-case method and path (relative to
    /// the gateway's base path, as the document writes it), which is what the tools are expected to be named by.
    /// </summary>
    private async Task<Dictionary<(string Method, string Path), string>> ReadOperationIdsAsync()
    {
        using var http = system.Gateway.CreateClient();
        using var document = JsonDocument.Parse(await http.GetStringAsync("gateway/.blueprint/openapi.json"));

        var operationIds = new Dictionary<(string Method, string Path), string>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                operationIds[(operation.Name, path.Name.ToLowerInvariant())] = operation.Value.GetProperty("operationId").GetString()!;
            }
        }

        return operationIds;
    }

    private async Task<McpClient> ConnectAsync(string? bearerToken)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(system.Gateway.BaseAddress, "gateway/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        if (bearerToken is not null)
        {
            options.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" };
        }

        return await McpClient.CreateAsync(new HttpClientTransport(options));
    }
}
