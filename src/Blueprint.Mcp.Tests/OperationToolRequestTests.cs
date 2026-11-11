using System.Text;
using System.Text.Json;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Blueprint.Mcp.Tests;

public sealed class OperationToolRequestTests
{
    [Fact]
    public void A_path_argument_is_written_into_the_path_and_its_route_value_as_a_server_and_routing_give_them()
    {
        var request = Create(
            "api/templates/{Id}",
            [new OperationInputProperty("Id", OperationInputLocation.Path)],
            """{ "Id": "a b/c" }""");

        request.Path.Should().Be("/api/templates/a b%2Fc");
        request.RouteValues["Id"].Should().Be("a b%2Fc");
    }

    [Fact]
    public void A_route_value_no_input_supplies_is_taken_from_the_route_the_tools_were_called_at()
    {
        var request = OperationToolRequest.Create(
            RoutePatternFactory.Parse("tenants/{TenantId}/api/templates/{Id}"),
            Schema([new OperationInputProperty("Id", OperationInputLocation.Path)]),
            Arguments("""{ "Id": "a" }"""),
            new RouteValueDictionary { ["TenantId"] = "acme", ["Id"] = "not this" },
            out var error);

        request.Should().NotBeNull(error);
        request!.Path.Should().Be("/tenants/acme/api/templates/a");
        request.RouteValues["TenantId"].Should().Be("acme");
        request.RouteValues["Id"].Should().Be("a");
    }

    [Fact]
    public void An_omitted_defaulted_path_argument_is_given_its_default_as_a_route_value_as_routing_gives_it()
    {
        var request = Create("api/defaulted/{Kind=all}", [new OperationInputProperty("Kind", OperationInputLocation.Path)], "{}");

        request.Path.Should().Be("/api/defaulted/");
        request.RouteValues["Kind"].Should().Be("all");
    }

    [Theory]
    [InlineData("example.com", "http", "example.com", true)]
    [InlineData("EXAMPLE.com:80", "http", "example.com", true)]
    [InlineData("other.com", "http", "example.com", false)]
    [InlineData("api.example.com", "https", "*.example.com", true)]
    [InlineData("example.com", "https", "*.example.com", false)]
    [InlineData("example.com:8080", "http", "example.com:8080", true)]
    [InlineData("example.com", "https", "example.com:443", true)]
    [InlineData("example.com", "http", "example.com:8080", false)]
    [InlineData("anything.test:5000", "http", "*:5000", true)]
    [InlineData("[::1]:5000", "http", "[::1]", true)]
    [InlineData("[::1]:5000", "http", "[::1]:5000", true)]
    [InlineData("[::1]:5000", "http", "[::1]:6000", false)]
    [InlineData("[::1]", "https", "[::1]:443", true)]
    public void A_host_is_matched_against_RequireHost_patterns_as_routing_matches_it(string requestHost, string scheme, string allowed, bool matches)
    {
        var request = new Microsoft.AspNetCore.Http.DefaultHttpContext().Request;
        request.Host = new Microsoft.AspNetCore.Http.HostString(requestHost);
        request.Scheme = scheme;

        OperationToolRequest.MatchesHost(new Microsoft.AspNetCore.Routing.HostAttribute(allowed), request).Should().Be(matches);
    }

    [Theory]
    [InlineData("""{ "Name": "abc" }""", "/api/files/abc", null)]
    [InlineData("""{ "Name": "abc", "Ext": "txt" }""", "/api/files/abc.txt", "txt")]
    public void An_optional_value_behind_a_separator_and_its_separator_are_left_out_when_it_is_not_given(string arguments, string path, string? ext)
    {
        var request = Create(
            "api/files/{Name}.{Ext?}",
            [new OperationInputProperty("Name", OperationInputLocation.Path), new OperationInputProperty("Ext", OperationInputLocation.Path)],
            arguments);

        request.Path.Should().Be(path);
        request.RouteValues["Name"].Should().Be("abc");
        request.RouteValues.GetValueOrDefault("Ext").Should().Be(ext);
    }

    [Fact]
    public void A_catch_all_path_argument_keeps_its_slashes()
    {
        var request = Create(
            "files/{**Path}",
            [new OperationInputProperty("Path", OperationInputLocation.Path)],
            """{ "Path": "a/b c" }""");

        request.Path.Should().Be("/files/a/b c");
    }

    [Fact]
    public void A_missing_path_argument_makes_no_request_and_says_which_argument_is_missing()
    {
        var request = OperationToolRequest.Create(
            RoutePatternFactory.Parse("api/templates/{Id}"),
            Schema([new OperationInputProperty("Id", OperationInputLocation.Path)]),
            Arguments("{}"),
            null,
            out var error);

        request.Should().BeNull();
        error.Should().Contain("'Id'");
    }

    [Fact]
    public void Query_arguments_are_written_as_an_HTTP_caller_writes_them()
    {
        var request = Create(
            "api/templates",
            [
                new OperationInputProperty("name", OperationInputLocation.Query),
                new OperationInputProperty("take", OperationInputLocation.Query),
                new OperationInputProperty("archived", OperationInputLocation.Query),
                new OperationInputProperty("tags", OperationInputLocation.Query),
                new OperationInputProperty("missing", OperationInputLocation.Query),
                new OperationInputProperty("none", OperationInputLocation.Query),
            ],
            """{ "name": "a&b", "take": 10, "archived": true, "tags": ["x", "y"], "none": null }""");

        request.QueryString.Value.Should().Be("?name=a%26b&take=10&archived=true&tags=x&tags=y");
        request.Body.Should().BeNull();
    }

    [Fact]
    public void Header_arguments_become_headers_with_one_value_per_array_element()
    {
        var request = Create(
            "api/templates",
            [
                new OperationInputProperty("X-Tenant", OperationInputLocation.Header),
                new OperationInputProperty("X-Tags", OperationInputLocation.Header),
            ],
            """{ "X-Tenant": "t1", "X-Tags": ["a", "b"] }""");

        request.Headers.Should().BeEquivalentTo(
            new Dictionary<string, string[]> { ["X-Tenant"] = ["t1"], ["X-Tags"] = ["a", "b"] }.Select(static p =>
                new KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>(p.Key, p.Value)));
    }

    [Fact]
    public void Body_arguments_are_the_JSON_body_as_the_values_they_are_and_an_explicit_null_is_kept()
    {
        var request = Create(
            "api/templates",
            [
                new OperationInputProperty("name", OperationInputLocation.Body),
                new OperationInputProperty("size", OperationInputLocation.Body),
                new OperationInputProperty("owner", OperationInputLocation.Body),
                new OperationInputProperty("description", OperationInputLocation.Body),
                new OperationInputProperty("unset", OperationInputLocation.Body),
            ],
            """{ "name": "n", "size": 3, "owner": { "id": 1 }, "description": null, "unknown": 1 }""");

        Encoding.UTF8.GetString(request.Body!).Should().Be("""{"name":"n","size":3,"owner":{"id":1},"description":null}""");
    }

    [Fact]
    public void An_operation_that_reads_a_body_is_sent_an_empty_object_when_no_body_argument_is_given()
    {
        // As a JSON client posts {}, so binding and validation see an empty body rather than no body at all.
        var request = Create("api/templates", [new OperationInputProperty("name", OperationInputLocation.Body)], "{}");

        Encoding.UTF8.GetString(request.Body!).Should().Be("{}");
    }

    [Fact]
    public void An_argument_is_matched_to_its_property_ignoring_case()
    {
        var request = Create(
            "api/templates/{Id}",
            [new OperationInputProperty("Id", OperationInputLocation.Path), new OperationInputProperty("name", OperationInputLocation.Query)],
            """{ "id": "abc", "NAME": "n" }""");

        request.Path.Should().Be("/api/templates/abc");
        request.QueryString.Value.Should().Be("?name=n");
    }

    [Fact]
    public void An_operation_with_no_inputs_gets_its_literal_path_and_nothing_else()
    {
        var request = Create("api/templates", [], null);

        request.Path.Should().Be("/api/templates");
        request.QueryString.HasValue.Should().BeFalse();
        request.Headers.Should().BeEmpty();
        request.Body.Should().BeNull();
    }

    private static OperationToolRequest Create(string route, OperationInputProperty[] properties, string? arguments)
    {
        var request = OperationToolRequest.Create(
            RoutePatternFactory.Parse(route),
            Schema(properties),
            arguments is null ? null : Arguments(arguments),
            null,
            out var error);

        request.Should().NotBeNull(error);
        return request!;
    }

    private static OperationInputSchema Schema(OperationInputProperty[] properties)
    {
        return new OperationInputSchema("Op", "op", JsonDocument.Parse("""{ "type": "object" }""").RootElement, [.. properties]);
    }

    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        return JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(static p => p.Name, static p => p.Value.Clone());
    }
}
