using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Blueprint.Mcp.Tests;

/// <summary>
/// Calls the test app's operations as MCP tools, over HTTP, and compares each result with the response the same call
/// gets from the operation's HTTP endpoint.
/// </summary>
public sealed class McpToolCallTests
{
    [Fact]
    public async Task Two_operations_whose_names_differ_only_in_case_are_two_tools()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        await using var client = await host.ConnectAsync();

        var upper = await client.CallToolAsync(ids[("get", "/case/upper")], new Dictionary<string, object?>());
        var lower = await client.CallToolAsync(ids[("get", "/case/lower")], new Dictionary<string, object?>());

        Text(upper).Should().Be("\"upper\"");
        Text(lower).Should().Be("\"lower\"");
    }

    [Fact]
    public async Task With_rendering_deferred_an_app_mapped_before_another_still_has_its_own_tools()
    {
        IList<McpClientTool> testAppToolsAlone;

        await using (var alone = await McpTestHost.StartAsync())
        await using (var aloneClient = await alone.ConnectAsync())
        {
            testAppToolsAlone = await aloneClient.ListToolsAsync();
        }

        await using var host = await McpTestHost.StartAsync(renderOnFirstRequest: true, mapSampleApp: true);
        var sampleAppIds = await host.GetOperationIdsAsync("sample/");
        await using var testAppClient = await host.ConnectAsync("api/mcp");
        await using var sampleAppClient = await host.ConnectAsync("sample/mcp");

        var testAppTools = await testAppClient.ListToolsAsync();
        var sampleAppTools = await sampleAppClient.ListToolsAsync();

        testAppTools.Select(static t => t.Name).Should().BeEquivalentTo(testAppToolsAlone.Select(static t => t.Name));
        sampleAppTools.Should().NotBeEmpty();
        sampleAppTools.Select(static t => t.Name).Should().BeSubsetOf(sampleAppIds.Values);
    }

    [Fact]
    public async Task An_operation_answering_GET_and_POST_is_called_at_the_method_its_schema_describes()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        await using var client = await host.ConnectAsync();

        var result = await client.CallToolAsync(
            ids[("post", "/multi-method")],
            new Dictionary<string, object?> { ["name"] = "echo", ["count"] = 2 });

        result.IsError.Should().NotBe(true);
        Text(result).Should().Be("\"echo:2\"");
    }

    [Fact]
    public async Task A_handler_reading_the_request_from_IHttpContextAccessor_is_given_the_operations_request()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync();

        var overHttp = await http.GetStringAsync("api/accessor/abc");
        var result = await client.CallToolAsync(ids[("get", "/accessor/{Id}")], new Dictionary<string, object?> { ["Id"] = "abc" });

        overHttp.Should().Be("\"/api/accessor/abc\"");
        Text(result).Should().Be(overHttp);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("User", 403)]
    [InlineData("Admin", 200)]
    public async Task An_ASP_NET_Core_policy_on_the_operations_endpoint_decides_a_tool_call_as_it_decides_HTTP(string? roles, int expectedStatus)
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient(roles);
        await using var client = await host.ConnectAsync(roles: roles);

        using var overHttp = await http.GetAsync("api/guarded");
        var result = await client.CallToolAsync(ids[("get", "/guarded")], new Dictionary<string, object?>());

        ((int)overHttp.StatusCode).Should().Be(expectedStatus);
        HttpStatus(result).Should().Be(expectedStatus);
        result.IsError.Should().Be(expectedStatus >= 400);
        (Text(result) ?? string.Empty).Should().Be(await overHttp.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("User", 403)]
    [InlineData("Blue", 200)]
    public async Task An_authorisation_requirement_an_endpoint_attribute_supplies_decides_a_tool_call_as_it_decides_HTTP(string roles, int expectedStatus)
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient(roles);
        await using var client = await host.ConnectAsync(roles: roles);

        using var overHttp = await http.GetAsync("api/requirement");
        var result = await client.CallToolAsync(ids[("get", "/requirement")], new Dictionary<string, object?>());

        ((int)overHttp.StatusCode).Should().Be(expectedStatus);
        HttpStatus(result).Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("User", 403)]
    [InlineData("Admin", 200)]
    public async Task The_fallback_policy_decides_a_tool_call_to_an_endpoint_without_authorisation_metadata_as_it_decides_HTTP(string roles, int expectedStatus)
    {
        await using var host = await McpTestHost.StartAsync(adminFallback: true);
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient(roles);
        await using var client = await host.ConnectAsync(roles: roles);

        using var overHttp = await http.GetAsync("api/case/upper");
        var result = await client.CallToolAsync(ids[("get", "/case/upper")], new Dictionary<string, object?>());

        ((int)overHttp.StatusCode).Should().Be(expectedStatus);
        HttpStatus(result).Should().Be(expectedStatus);
    }

    [Fact]
    public async Task An_app_mapped_twice_is_called_through_the_mapping_its_tools_were_mapped_after()
    {
        await using var host = await McpTestHost.StartAsync(mapAdminCopy: true);
        var ids = await host.GetOperationIdsAsync();
        await using var publicClient = await host.ConnectAsync("api/mcp", roles: "User");
        await using var adminClient = await host.ConnectAsync("admin/mcp", roles: "User");
        await using var adminAsAdminClient = await host.ConnectAsync("admin/mcp", roles: "Admin");
        var accessorTool = ids[("get", "/accessor/{Id}")];
        var arguments = new Dictionary<string, object?> { ["Id"] = "abc" };

        var overPublic = await publicClient.CallToolAsync(accessorTool, arguments);
        var overAdmin = await adminClient.CallToolAsync(accessorTool, arguments);
        var overAdminAsAdmin = await adminAsAdminClient.CallToolAsync(accessorTool, arguments);

        Text(overPublic).Should().Be("\"/api/accessor/abc\"");
        HttpStatus(overAdmin).Should().Be(403);
        Text(overAdminAsAdmin).Should().Be("\"/admin/accessor/abc\"");
    }

    [Fact]
    public async Task A_handlers_response_callbacks_run_and_its_registered_resources_are_disposed()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        var probe = host.Services.GetRequiredService<App.ResponseCallbackProbe>();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync();

        // The tool call first: a server disposes the HTTP request's resources after it has sent the response, so they
        // may still be being disposed when the client has it.
        var result = await client.CallToolAsync(ids[("get", "/callbacks")], new Dictionary<string, object?>());
        var disposedByToolCall = probe.Disposed;
        using var overHttp = await http.GetAsync("api/callbacks");

        disposedByToolCall.Should().Be(1);
        ((int)overHttp.StatusCode).Should().Be(203);
        HttpStatus(result).Should().Be(203);
        Text(result).Should().Be(await overHttp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Middleware_around_the_MCP_endpoint_still_reads_its_own_request_from_IHttpContextAccessor_after_a_tool_call()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        var ambient = host.Services.GetRequiredService<McpTestHost.AmbientContextProbe>();
        await using var client = await host.ConnectAsync();

        await client.CallToolAsync(ids[("get", "/accessor/{Id}")], new Dictionary<string, object?> { ["Id"] = "abc" });
        await ambient.WaitForIdleAsync();

        ambient.Lost.Should().BeEmpty();
    }

    [Fact]
    public async Task An_OnCompleted_callback_reads_the_operations_request_from_IHttpContextAccessor()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        var probe = host.Services.GetRequiredService<App.ResponseCallbackProbe>();
        await using var client = await host.ConnectAsync();

        await client.CallToolAsync(ids[("get", "/callbacks")], new Dictionary<string, object?>());

        probe.CompletedPath.Should().Be("/api/callbacks");
    }

    [Theory]
    [InlineData("User", 403)]
    [InlineData("Admin", 200)]
    public async Task An_app_mapped_in_a_route_group_is_called_with_the_groups_prefix_and_conventions(string roles, int expectedStatus)
    {
        await using var host = await McpTestHost.StartAsync(mapGrouped: true);
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient(roles);
        await using var client = await host.ConnectAsync("grouped/api/mcp", roles: roles);

        using var overHttp = await http.GetAsync("grouped/api/accessor/abc");
        var result = await client.CallToolAsync(ids[("get", "/accessor/{Id}")], new Dictionary<string, object?> { ["Id"] = "abc" });

        ((int)overHttp.StatusCode).Should().Be(expectedStatus);
        HttpStatus(result).Should().Be(expectedStatus);
        (Text(result) ?? string.Empty).Should().Be(await overHttp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_app_mapped_in_a_route_group_with_a_route_value_is_called_with_the_value_the_tools_were_called_at()
    {
        await using var host = await McpTestHost.StartAsync(mapTenantGroup: true);
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync("tenants/acme/api/mcp");

        var overHttp = await http.GetStringAsync("tenants/acme/api/accessor/abc");
        var result = await client.CallToolAsync(ids[("get", "/accessor/{Id}")], new Dictionary<string, object?> { ["Id"] = "abc" });

        overHttp.Should().Be("\"/tenants/acme/api/accessor/abc\"");
        Text(result).Should().Be(overHttp);
    }

    [Fact]
    public async Task A_HEAD_operations_tool_result_carries_its_status_and_no_body_as_HTTP_does()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync();

        using var overHttp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "api/head"));
        var result = await client.CallToolAsync(ids[("head", "/head")], new Dictionary<string, object?>());

        ((int)overHttp.StatusCode).Should().Be(200);
        (await overHttp.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        HttpStatus(result).Should().Be(200);
        result.Content.Should().BeEmpty();
        result.StructuredContent.Should().BeNull();
    }

    [Fact]
    public async Task An_anonymous_endpoint_naming_a_scheme_is_given_the_user_that_scheme_authenticates_as_HTTP_gives_it()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Roles Admin");
        await using var client = await host.ConnectAsync(authorization: "Roles Admin");

        var overHttp = await http.GetStringAsync("api/who-am-i");
        var result = await client.CallToolAsync(ids[("get", "/who-am-i")], new Dictionary<string, object?>());

        overHttp.Should().Be("\"Admin\"");
        Text(result).Should().Be(overHttp);
    }

    [Theory]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("a b", "a%20b")]
    public async Task A_route_value_reaches_the_operation_as_HTTP_gives_it(string value, string escaped)
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync();

        var overHttp = await http.GetStringAsync("api/echo/" + escaped);
        var result = await client.CallToolAsync(ids[("get", "/echo/{Id}")], new Dictionary<string, object?> { ["Id"] = value });

        Text(result).Should().Be(overHttp);
    }

    [Theory]
    [InlineData("", "folders/")]
    [InlineData("a/b c", "folders/a/b%20c")]
    public async Task A_catch_all_route_value_reaches_the_operation_as_HTTP_gives_it_even_empty(string value, string url)
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync();

        var overHttp = await http.GetStringAsync("api/" + url);
        var result = await client.CallToolAsync(ids[("get", "/folders/{**FolderPath}")], new Dictionary<string, object?> { ["FolderPath"] = value });

        result.IsError.Should().NotBe(true);
        Text(result).Should().Be(overHttp);
    }

    [Fact]
    public async Task An_empty_route_value_is_refused_as_missing_as_no_HTTP_route_matches_it()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync();

        using var overHttp = await http.GetAsync("api/echo/");
        var result = await client.CallToolAsync(ids[("get", "/echo/{Id}")], new Dictionary<string, object?> { ["Id"] = string.Empty });

        ((int)overHttp.StatusCode).Should().Be(404);
        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("'Id'");
    }

    [Fact]
    public async Task Tools_mapped_after_another_apps_API_are_refused_at_mapping_even_with_rendering_deferred()
    {
        // Deferred, the tools are not made until the first request, so only a check at mapping refuses them at start-up
        // rather than serving another app's schemas through its endpoints and policies.
        var act = () => McpTestHost.StartAsync(renderOnFirstRequest: true, mapSampleAppBetween: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*McpTestBlueprintApp*MapBlueprintApi*");
    }

    [Fact]
    public async Task A_path_argument_reaches_the_handler_decoded_as_HTTP_gives_it()
    {
        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        using var http = host.CreateHttpClient();
        await using var client = await host.ConnectAsync();

        var overHttp = await http.GetStringAsync("api/accessor/a%20b");
        var result = await client.CallToolAsync(ids[("get", "/accessor/{Id}")], new Dictionary<string, object?> { ["Id"] = "a b" });

        overHttp.Should().Be("\"/api/accessor/a b\"");
        Text(result).Should().Be(overHttp);
    }

    [Fact]
    public async Task A_file_response_is_described_by_its_size()
    {
        const int size = 3 * 1024 * 1024;

        await using var host = await McpTestHost.StartAsync();
        var ids = await host.GetOperationIdsAsync();
        await using var client = await host.ConnectAsync();

        var result = await client.CallToolAsync(ids[("get", "/download")], new Dictionary<string, object?> { ["Size"] = size });

        HttpStatus(result).Should().Be(200);
        Text(result).Should().Be($"The operation returned {size} bytes of application/octet-stream, which a tool result does not carry.");
    }

    private static string? Text(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().SingleOrDefault()?.Text;
    }

    private static int HttpStatus(CallToolResult result)
    {
        return result.Meta![OperationToolResult.HttpStatusMetaKey]!.GetValue<int>();
    }
}
