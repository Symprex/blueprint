using ModelContextProtocol.Protocol;

namespace Blueprint.Mcp.Tests;

/// <summary>
/// The tests that set ASP.NET Core's process-wide switch making the endpoint, not the request, the resource authorised.
/// They run alone, so no other test sees the switch set.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuthorizationResourceSwitchCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Authorization resource switch";
}

[Collection(AuthorizationResourceSwitchCollection.Name)]
public sealed class AuthorizationResourceSwitchTests
{
    private const string Switch = "Microsoft.AspNetCore.Authorization.SuppressUseHttpContextAsAuthorizationResource";

    [Fact]
    public async Task With_the_endpoint_as_the_authorisation_resource_a_handler_of_endpoints_decides_a_tool_call_as_it_decides_HTTP()
    {
        AppContext.SetSwitch(Switch, true);

        try
        {
            await using var host = await McpTestHost.StartAsync(endpointResourceUpperCase: true);
            var ids = await host.GetOperationIdsAsync();
            using var http = host.CreateHttpClient("User");
            await using var client = await host.ConnectAsync(roles: "User");

            using var overHttp = await http.GetAsync("api/case/upper");
            var result = await client.CallToolAsync(ids[("get", "/case/upper")], new Dictionary<string, object?>());

            ((int)overHttp.StatusCode).Should().Be(200);
            result.Meta![OperationToolResult.HttpStatusMetaKey]!.GetValue<int>().Should().Be(200);
            result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("\"upper\"");
        }
        finally
        {
            AppContext.SetSwitch(Switch, false);
        }
    }
}
