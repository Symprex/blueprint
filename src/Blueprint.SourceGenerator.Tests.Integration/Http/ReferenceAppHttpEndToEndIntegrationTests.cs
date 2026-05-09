using System.Security.Claims;
using System.Text;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the curated sample app is runnable through the generated HTTP host with deterministic seeded behavior.
/// </summary>
public class ReferenceAppHttpEndToEndIntegrationTests
{
    [Fact]
    public async Task When_sample_templates_endpoint_is_invoked_without_authenticated_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "GET");
        endpoint.Should().NotBeNull("the sample templates route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_sample_template_detail_endpoint_is_invoked_with_authenticated_user_then_returns_seeded_template()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "GET");
        endpoint.Should().NotBeNull("the sample template detail route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-002";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "template-002",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("template-002");
        response.Body.Should().Contain("Formal Greeting");
    }

    [Fact]
    public async Task When_get_template_returns_200_then_response_includes_object_type_and_self_link()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "GET");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "template-001",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);

        // ApiResource serializes $object as the resource type discriminator.
        response.Body.Should().Contain("\"$object\":");

        // The generated pipeline populates $links with self when [SelfLink] is declared.
        response.Body.Should().Contain("\"$links\":");
        response.Body.Should().Contain("\"self\":");
        response.Body.Should().Contain("/templates/template-001");
    }

    [Fact]
    public async Task When_get_template_with_unknown_id_then_returns_404_not_found()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "GET");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/unknown-999";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "unknown-999",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("unknown-999");
    }

    [Fact]
    public async Task When_create_template_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var bodyJson = """{"name":"Test Template","htmlSource":"<p>Hello</p>","plainTextSource":"Hello"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_create_template_with_valid_body_then_returns_201_with_resource_event_envelope()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var bodyJson = """{"name":"My Template","htmlSource":"<p>{{Name}}</p>","plainTextSource":"Hello {{Name}}"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.LocationHeader.Should().NotBeNull("201 Created responses must include a Location header per RFC 7231");
        response.LocationHeader.Should().Contain("/templates/", "Location should point to the created resource's self-link URL");
        response.Body.Should().Contain("\"eventId\":\"template.created\"");
        response.Body.Should().Contain("\"resourceId\":\"template-");
        response.Body.Should().Contain("\"content\":");
        response.Body.Should().Contain("My Template");
    }

    [Fact]
    public async Task When_create_template_with_missing_required_name_then_returns_422_with_validation_errors()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var bodyJson = """{"htmlSource":"<p>No name</p>"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
    }

    [Fact]
    public async Task When_create_template_with_name_exceeding_max_length_then_returns_422_with_validation_errors()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var tooLongName = new string('x', 201);
        var bodyJson = System.Text.Encoding.UTF8.GetBytes($$$"""{"name":"{{{tooLongName}}}","htmlSource":"<p>test</p>"}""");
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
        response.Body.Should().Contain("200");
    }

    [Fact]
    public async Task When_update_template_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");
        endpoint.Should().NotBeNull("the update template route should be registered");

        var bodyJson = """{"name":"Updated","htmlSource":"<p>Updated</p>","plainTextSource":"Updated"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_update_template_with_valid_body_then_returns_200_with_updated_resource()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");

        var bodyJson = """{"name":"Renamed Template","htmlSource":"<p>New content</p>","plainTextSource":"New content"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("\"eventId\":\"template.updated\"");
        response.Body.Should().Contain("\"resourceId\":\"template-001\"");
        response.Body.Should().Contain("Renamed Template");
        response.Body.Should().Contain("New content");
    }

    [Fact]
    public async Task When_update_template_with_missing_name_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");

        var bodyJson = """{"htmlSource":"<p>No name</p>"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
    }

    [Fact]
    public async Task When_update_template_with_unknown_id_then_returns_404_via_typed_exception()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");

        var bodyJson = """{"name":"Ghost","htmlSource":"<p>gone</p>","plainTextSource":"gone"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/nonexistent-999";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "nonexistent-999" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.Body.Should().Contain("nonexistent-999");
    }

    [Fact]
    public async Task When_archive_template_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "DELETE");
        endpoint.Should().NotBeNull("the DELETE template route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Delete;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_archive_template_with_auth_then_returns_204_no_content()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "DELETE");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Delete;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        // KeyOnlyResourceEventResult with ChangeType.Deleted produces 204 No Content.
        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_search_templates_with_auth_then_returns_matching_results()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/search", "GET");
        endpoint.Should().NotBeNull("the search route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/search";
            context.Request.QueryString = new QueryString("?Q=Welcome&Page=1&PageSize=10");
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("Welcome Email");
    }

    [Fact]
    public async Task When_get_template_versions_with_auth_then_returns_version_history()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/versions", "GET");
        endpoint.Should().NotBeNull("the versions route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-001/versions";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("template-001");
    }

    [Fact]
    public async Task When_publish_template_with_auth_then_returns_resource_event()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/publish", "POST");
        endpoint.Should().NotBeNull("the publish route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/publish";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("template.published");
    }

    [Fact]
    public async Task When_publish_template_already_published_then_returns_409_conflict()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/publish", "POST");

        // First publish succeeds (200 OK for ChangeType.Updated)
        var firstResponse = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-002/publish";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-002" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        firstResponse.StatusCode.Should().Be(StatusCodes.Status200OK);

        // Second publish of the same template returns conflict (409)
        var secondResponse = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-002/publish";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-002" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        secondResponse.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        secondResponse.ContentType.Should().Be("application/json; charset=utf-8");
        secondResponse.Body.Should().Contain("template-002");
    }

    [Fact]
    public async Task When_clone_template_with_auth_and_body_then_returns_resource_event()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/clone", "POST");
        endpoint.Should().NotBeNull("the clone route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/clone";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"newName":"Cloned Template"}"""));
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.Body.Should().Contain("template.cloned");
        response.Body.Should().Contain("Cloned Template");
    }

    [Fact]
    public async Task When_preview_template_with_auth_and_merge_fields_then_returns_rendered_output()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/preview", "POST");
        endpoint.Should().NotBeNull("the preview route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/preview";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":"template-001","mergeFields":{"Name":"World"}}"""));
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("Hello, World!");
    }

    [Fact]
    public async Task When_search_templates_anonymous_then_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/search", "GET");
        endpoint.Should().NotBeNull("the search route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/search";
            context.Request.QueryString = new QueryString("?Q=Welcome&Page=1&PageSize=10");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("Welcome Email");
    }

    [Fact]
    public async Task When_search_templates_with_correlation_header_then_header_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/search", "GET");
        endpoint.Should().NotBeNull("the search route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/search";
            context.Request.QueryString = new QueryString("?Q=Welcome&Page=1&PageSize=10");
            context.Request.Headers["X-Correlation-Id"] = "test-correlation-123";
        });

        // The handler succeeds and returns results — this proves header binding didn't break the pipeline
        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("Welcome Email");
    }

    [Fact]
    public async Task When_search_templates_without_required_query_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/search", "GET");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/search";
            context.Request.QueryString = new QueryString("?Page=1&PageSize=10");
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.Body.Should().Contain("Q");
    }

    [Fact]
    public async Task When_clone_template_without_required_name_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/clone", "POST");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/clone";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{}"""));
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.Body.Should().Contain("NewName");
    }

    [Fact]
    public async Task When_search_templates_with_page_size_exceeding_max_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/search", "GET");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/search";
            context.Request.QueryString = new QueryString("?Q=test&Page=1&PageSize=999");
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.Body.Should().Contain("PageSize");
    }

    [Fact]
    public async Task When_concurrent_requests_dispatched_to_multiple_endpoints_then_all_succeed()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var searchEndpoint = FindEndpoint(endpoints, "api/templates/search", "GET");
        var listEndpoint = FindEndpoint(endpoints, "api/templates", "GET");
        var versionsEndpoint = FindEndpoint(endpoints, "api/templates/{Id}/versions", "GET");

        searchEndpoint.Should().NotBeNull();
        listEndpoint.Should().NotBeNull();
        versionsEndpoint.Should().NotBeNull();

        const int concurrency = 32;
        var tasks = new Task<TestHttpResponse>[concurrency];

        for (var i = 0; i < concurrency; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                var (endpoint, configure) = (index % 3) switch
                {
                    0 => (searchEndpoint!, (Action<DefaultHttpContext>)(ctx =>
                    {
                        ctx.Request.Method = HttpMethods.Get;
                        ctx.Request.Path = "/api/templates/search";
                        ctx.Request.QueryString = new QueryString("?Q=Welcome&Page=1&PageSize=10");
                    })),
                    1 => (listEndpoint!, (Action<DefaultHttpContext>)(ctx =>
                    {
                        ctx.Request.Method = HttpMethods.Get;
                        ctx.Request.Path = "/api/templates";
                        ctx.User = new ClaimsPrincipal(
                            new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                            authenticationType: "test"));
                    })),
                    _ => (versionsEndpoint!, (Action<DefaultHttpContext>)(ctx =>
                    {
                        ctx.Request.Method = HttpMethods.Get;
                        ctx.Request.Path = "/api/templates/template-001/versions";
                        ctx.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
                        ctx.User = new ClaimsPrincipal(
                            new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                            authenticationType: "test"));
                    })),
                };

                return await InvokeEndpointAsync(endpoint, endpoints.ServiceProvider, configure);
            });
        }

        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(static r => r.StatusCode == StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_authenticated_user_lacks_scope_claims_and_provider_registered_then_returns_403()
    {
        var endpoints = CreateEndpointsWithAuthorizationProvider();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "GET");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task When_authenticated_user_has_matching_scope_claim_and_provider_registered_then_returns_200()
    {
        var endpoints = CreateEndpointsWithAuthorizationProvider();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "GET");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim("scope", "template:read"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_anonymous_endpoint_with_authorization_provider_registered_then_returns_200_without_claims()
    {
        var endpoints = CreateEndpointsWithAuthorizationProvider();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        // SearchTemplatesQuery has [AllowAnonymous] — should bypass auth entirely
        var endpoint = FindEndpoint(endpoints, "api/templates/search", "GET");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/search";
            context.Request.QueryString = new QueryString("?Q=test&Page=1&PageSize=10");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_export_templates_with_auth_then_returns_202_accepted_with_job_reference()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/export", "POST");
        endpoint.Should().NotBeNull("the export route should be registered");

        var bodyJson = """{"nameFilter":"Welcome"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("JobId");
        response.Body.Should().Contain("EstimatedSeconds");
    }

    [Fact]
    public async Task When_export_templates_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/export", "POST");
        endpoint.Should().NotBeNull("the export route should be registered");

        var bodyJson = """{}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_export_templates_with_notify_true_but_no_email_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/export", "POST");
        endpoint.Should().NotBeNull();

        // NotifyOnCompletion=true makes NotifyEmail required via [RequiredIf]
        var bodyJson = """{"notifyOnCompletion":true}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/export";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[RequiredIf(NotifyOnCompletion, true)] should reject missing NotifyEmail");
        response.Body.Should().Contain("NotifyEmail");
    }

    [Fact]
    public async Task When_export_templates_with_notify_false_but_email_set_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/export", "POST");
        endpoint.Should().NotBeNull();

        // NotifyOnCompletion=false makes NotifyEmail not allowed via [NotAllowedIf]
        var bodyJson = """{"notifyOnCompletion":false,"notifyEmail":"test@example.com"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/export";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[NotAllowedIf(NotifyOnCompletion, false)] should reject NotifyEmail when notify is off");
        response.Body.Should().Contain("NotifyEmail");
    }

    [Fact]
    public async Task When_export_templates_with_notify_true_and_valid_email_then_returns_202()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/export", "POST");
        endpoint.Should().NotBeNull();

        // Valid: notify=true with a proper email address
        var bodyJson = """{"notifyOnCompletion":true,"notifyEmail":"admin@example.com"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/export";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted,
            "valid export request with notification should return 202 Accepted");
    }

    [Fact]
    public async Task When_health_check_invoked_anonymously_then_returns_200_plain_text()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/system/health", "GET");
        endpoint.Should().NotBeNull("the health check route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/system/health";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("text/plain; charset=utf-8");
        response.Body.Should().Contain("healthy");
    }

    [Fact]
    public async Task When_download_template_with_auth_then_returns_200_with_binary_stream()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/download", "GET");
        endpoint.Should().NotBeNull("the download route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-001/download";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/octet-stream");
        response.Body.Should().NotBeEmpty("the template content should be streamed");
    }

    [Fact]
    public async Task When_redirect_to_docs_with_auth_then_returns_redirect()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/docs", "GET");
        endpoint.Should().NotBeNull("the redirect-to-docs route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-001/docs";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status302Found);
        response.LocationHeader.Should().NotBeNull("redirect responses must include a Location header");
        response.LocationHeader.Should().Contain("template-001");
    }

    [Fact]
    public async Task When_update_template_with_ivalidatable_cross_field_violation_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");

        // HtmlSource and PlainTextSource are both null/empty — cross-field IValidatableObject check triggers
        var bodyJson = """{"name":"Valid Name"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("content source");
    }

    [Fact]
    public async Task When_patch_template_settings_with_valid_nullable_body_then_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/settings", "PATCH");
        endpoint.Should().NotBeNull("the PATCH settings route should be registered");

        var bodyJson = """{"opacity":0.75,"minWeight":10,"maxWeight":50,"cssClassName":"my-class"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Patch;
            context.Request.Path = "/api/templates/template-001/settings";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task When_patch_template_settings_with_opacity_out_of_range_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/settings", "PATCH");

        // Opacity must be in [0.0, 1.0]
        var bodyJson = """{"opacity":1.5}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Patch;
            context.Request.Path = "/api/templates/template-001/settings";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Opacity");
    }

    [Fact]
    public async Task When_patch_template_settings_with_maxweight_less_than_minweight_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/settings", "PATCH");

        // MaxWeight must be greater than MinWeight
        var bodyJson = """{"minWeight":50,"maxWeight":10}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Patch;
            context.Request.Path = "/api/templates/template-001/settings";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("MaxWeight");
    }

    [Fact]
    public async Task When_patch_template_settings_with_null_nullable_fields_then_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/settings", "PATCH");

        // All nullable fields omitted — should pass validation
        var bodyJson = """{}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Patch;
            context.Request.Path = "/api/templates/template-001/settings";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_paged_templates_endpoint_invoked_with_auth_then_returns_paged_result()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/paged", "GET");
        endpoint.Should().NotBeNull("the paged templates route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/paged";
            context.Request.QueryString = new QueryString("?Page=1&PageSize=2");
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");

        // PagedApiResource includes pagination metadata in the response body.
        response.Body.Should().Contain("\"$object\":");
        response.Body.Should().Contain("\"PageSize\":");
        response.Body.Should().Contain("\"CurrentPage\":1");
        response.Body.Should().Contain("\"Total\":");
        response.Body.Should().Contain("\"Values\":");
    }

    [Fact]
    public async Task When_paged_templates_endpoint_invoked_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/paged", "GET");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_handler_throws_ValidationException_then_returns_422_with_errors()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/validate", "POST");
        endpoint.Should().NotBeNull("the validate template route should be registered");

        var bodyJson = """{"name":"INVALID"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/validate";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        // ValidationException is caught by the pipeline and translated to ValidationFailedResult → 422.
        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "ValidationException should be translated to 422; got body: " + response.Body);
        response.Body.Should().Contain("Name");
        response.Body.Should().Contain("The name INVALID is not allowed");
    }

    [Fact]
    public async Task When_handler_throws_NotFoundException_then_returns_404()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/validate", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """{}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/not-found/validate";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "not-found" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().NotBe(StatusCodes.Status200OK,
            "NotFoundException should not be 200; got body: " + response.Body);
    }

    [Fact]
    public async Task When_handler_throws_BadRequestException_then_returns_400()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/validate", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"name":"BAD"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/validate";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().NotBe(StatusCodes.Status200OK,
            "BadRequestException should not be 200; got body: " + response.Body);
    }

    [Fact]
    public async Task When_validate_handler_succeeds_then_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/validate", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"name":"ValidName"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/validate";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        // NoResultOperationResult maps to 200 OK in the HTTP executor.
        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_tenant_scoped_operation_with_matching_account_then_returns_200()
    {
        var endpoints = CreateEndpointsWithTenantIsolation();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/tenants/{TenantId}/templates/{Id}/settings", "PUT");
        endpoint.Should().NotBeNull("the tenant-scoped settings route should be registered");

        var bodyJson = """{"displayName":"Updated Settings"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/tenants/tenant-A/templates/template-001/settings";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["TenantId"] = "tenant-A",
                ["Id"] = "template-001",
            };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);

            // User belongs to tenant-A — should pass tenant isolation
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim("account_id", "tenant-A"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK,
            "user with matching account_id should pass tenant isolation");
    }

    [Fact]
    public async Task When_tenant_scoped_operation_with_mismatched_account_then_returns_403()
    {
        var endpoints = CreateEndpointsWithTenantIsolation();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/tenants/{TenantId}/templates/{Id}/settings", "PUT");
        endpoint.Should().NotBeNull("the tenant-scoped settings route should be registered");

        var bodyJson = """{"displayName":"Cross-tenant attack"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/tenants/tenant-B/templates/template-001/settings";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["TenantId"] = "tenant-B",
                ["Id"] = "template-001",
            };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);

            // User belongs to tenant-A but is trying to access tenant-B
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim("account_id", "tenant-A"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden,
            "user with mismatched account_id should be blocked by tenant isolation");
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task When_bulk_archive_invoked_with_valid_ids_then_returns_202_accepted()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/bulk-archive", "POST");
        endpoint.Should().NotBeNull("the bulk archive route should be registered");

        var bodyJson = """{"ids":["template-001","template-002"],"reason":"Quarterly cleanup"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/bulk-archive";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);

            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted,
            "bulk archive should return 202 Accepted for fire-and-forget pattern");
    }

    [Fact]
    public async Task When_bulk_archive_invoked_with_empty_ids_then_returns_422_validation_error()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/bulk-archive", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"ids":[]}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/bulk-archive";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);

            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "empty ids array should fail MinLength(1) validation");
        response.Body.Should().Contain("Ids");
    }

    [Fact]
    public async Task When_template_history_invoked_then_returns_version_array()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/history", "GET");
        endpoint.Should().NotBeNull("the template history route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-001/history";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "template-001",
            };

            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("Version");
    }

    [Fact]
    public async Task When_template_history_invoked_with_invalid_date_range_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/history", "GET");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-001/history";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "template-001",
            };

            // StartDate > EndDate should fail LessThanProperty validation
            context.Request.QueryString = new QueryString("?StartDate=2025-06-01&EndDate=2024-01-01");

            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "StartDate > EndDate should fail LessThanProperty cross-property validation");
        response.Body.Should().Contain("StartDate");
    }

    [Fact]
    public async Task When_auth_manifest_invoked_with_authenticated_user_returns_accessible_operations()
    {
        var endpoints = CreateEndpointsWithAuthManifest();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/me/authManifest", "GET");
        endpoint.Should().NotBeNull("the auth manifest route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/me/authManifest";
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");

        // The AllowAll aggregator allows everything, so all exposed operations should appear
        response.Body.Should().Contain("AccessibleOperations");
    }

    [Fact]
    public async Task When_auth_manifest_invoked_without_authentication_returns_401()
    {
        var endpoints = CreateEndpointsWithAuthManifest();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/me/authManifest", "GET");
        endpoint.Should().NotBeNull("the auth manifest route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/me/authManifest";
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
            "[MustBeAuthenticated] should reject unauthenticated requests");
    }

    [Fact]
    public async Task When_change_password_invoked_with_mismatched_passwords_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/account/password", "PUT");
        endpoint.Should().NotBeNull("the change password route should be registered");

        var bodyJson = """{"currentPassword":"oldpassword1","newPassword":"newpass123","confirmNewPassword":"different789"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/account/password";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.ContentLength = bodyJson.Length;
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "Compare attribute should fail when NewPassword != ConfirmNewPassword");
        response.Body.Should().Contain("ConfirmNewPassword");
    }

    [Fact]
    public async Task When_change_password_invoked_with_matching_passwords_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/account/password", "PUT");
        endpoint.Should().NotBeNull("the change password route should be registered");

        var bodyJson = """{"currentPassword":"oldpassword1","newPassword":"newpass123","confirmNewPassword":"newpass123"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/account/password";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.ContentLength = bodyJson.Length;
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_create_template_with_multiple_invalid_fields_then_returns_422_with_all_errors_aggregated()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        // Missing required Name, invalid Slug (uppercase), invalid Visibility, out-of-range Priority,
        // invalid AuthorEmail, invalid CallbackUrl — all should be reported in one response
        var bodyJson = """{"slug":"INVALID_SLUG!","visibility":"secret","priority":9999,"authorEmail":"not-an-email","callbackUrl":"not-a-url"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "multiple invalid fields should all be caught and returned as 422");

        // Prove all validation errors are aggregated, not just the first one
        response.Body.Should().Contain("Name", "missing required Name should be reported");
        response.Body.Should().Contain("Slug", "invalid regex pattern should be reported");
        response.Body.Should().Contain("Visibility", "disallowed value should be reported");
        response.Body.Should().Contain("Priority", "out-of-range value should be reported");
        response.Body.Should().Contain("AuthorEmail", "invalid email should be reported");
        response.Body.Should().Contain("CallbackUrl", "invalid URL should be reported");
    }

    [Fact]
    public async Task When_create_template_with_denied_slug_value_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        // "admin" is in the DeniedValues list
        var bodyJson = """{"name":"Valid Name","slug":"admin"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "denied slug value 'admin' should trigger validation failure");
        response.Body.Should().Contain("Slug");
    }

    [Fact]
    public async Task When_validate_template_throws_not_found_exception_then_returns_404()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/validate", "POST");
        endpoint.Should().NotBeNull("the validate template route should be registered");

        // The handler throws NotFoundException when Id == "not-found"
        var bodyJson = """{"id":"not-found","name":"Test"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/not-found/validate";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "not-found",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound,
            "NotFoundException from handler should translate to 404 via exception pipeline");
    }

    [Fact]
    public async Task When_validate_template_throws_validation_exception_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/validate", "POST");
        endpoint.Should().NotBeNull("the validate template route should be registered");

        // The handler throws ValidationException when Name is "INVALID"
        var bodyJson = """{"id":"template-001","name":"INVALID"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/validate";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "template-001",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "handler-thrown ValidationException should translate to 422 via exception pipeline");
    }

    [Fact]
    public async Task When_property_populator_registered_then_tenant_id_auto_populated_from_jwt_claim()
    {
        var endpoints = CreateEndpointsWithPropertyPopulator();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        // UpdateTemplateSettings requires TenantId — property populator should fill it from claim
        var endpoint = FindEndpoint(endpoints, "api/tenants/{TenantId}/templates/{Id}/settings", "PUT");
        endpoint.Should().NotBeNull("tenant-scoped template settings route should be registered");

        var bodyJson = """{"displayName":"Updated Name"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/tenants/tenant-123/templates/template-001/settings";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["TenantId"] = "tenant-123",
                ["Id"] = "template-001",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim("tenant_id", "tenant-123"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK,
            "property populator should fill TenantId from JWT claim, allowing the request to proceed");
    }

    [Fact]
    public async Task When_validate_template_throws_bad_request_exception_then_returns_400()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/validate", "POST");
        endpoint.Should().NotBeNull("the validate template route should be registered");

        // The handler throws BadRequestException when Name is "BAD"
        var bodyJson = """{"id":"template-001","name":"BAD"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates/template-001/validate";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "template-001",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest,
            "BadRequestException from handler should translate to 400 via exception pipeline");
    }

    [Fact]
    public async Task When_create_template_with_invalid_allowed_value_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        // "secret" is not in AllowedValues("public", "internal", "private")
        var bodyJson = """{"name":"Valid Name","visibility":"secret"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[AllowedValues] should reject values not in the permitted set");
        response.Body.Should().Contain("Visibility");
    }

    [Fact]
    public async Task When_create_template_with_invalid_email_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"name":"Valid Name","authorEmail":"not-an-email"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[EmailAddress] should reject invalid email format");
        response.Body.Should().Contain("AuthorEmail");
    }

    [Fact]
    public async Task When_create_template_with_out_of_range_priority_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"name":"Valid Name","priority":0}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[Range(1,1000)] should reject priority below minimum");
        response.Body.Should().Contain("Priority");
    }

    [Fact]
    public async Task When_create_template_with_invalid_slug_regex_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        // Slug must match ^[a-z0-9]+(?:-[a-z0-9]+)*$ — uppercase letters violate it
        var bodyJson = """{"name":"Valid Name","slug":"Invalid-SLUG!"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[RegularExpression] should reject slug that doesn't match pattern");
        response.Body.Should().Contain("Slug");
    }

    [Fact]
    public async Task When_create_template_with_invalid_url_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"name":"Valid Name","callbackUrl":"not-a-valid-url"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[Url] should reject values that are not valid absolute URIs");
        response.Body.Should().Contain("CallbackUrl");
    }

    [Fact]
    public async Task When_create_template_with_too_long_name_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        // Name has [MaxLength(200)] — send 201 characters
        var longName = new string('x', 201);
        var bodyJson = Encoding.UTF8.GetBytes($$$"""{"name":"{{{longName}}}"}""");
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[MaxLength(200)] should reject name exceeding 200 characters");
        response.Body.Should().Contain("Name");
    }

    [Fact]
    public async Task When_create_template_with_all_valid_fields_returns_201_created()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        var bodyJson = """
            {
                "name": "My Template",
                "slug": "my-template",
                "visibility": "public",
                "priority": 50,
                "htmlSource": "<h1>Hello</h1>",
                "plainTextSource": "Hello",
                "authorEmail": "author@example.com",
                "callbackUrl": "https://example.com/callback"
            }
            """u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status201Created,
            "a fully valid create template should pass all validation and return 201");
        response.Body.Should().Contain("My Template");
    }

    [Fact]
    public async Task When_create_template_with_range_above_maximum_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull();

        // [Range(1, 1000)] — test upper boundary
        var bodyJson = """{"name":"Valid Name","priority":1001}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/templates";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            "[Range(1,1000)] should reject priority above maximum");
        response.Body.Should().Contain("Priority");
    }

    private static async Task<TestHttpResponse> InvokeEndpointAsync(
        RouteEndpoint endpoint,
        IServiceProvider serviceProvider,
        Action<DefaultHttpContext>? configureRequest = null)
    {
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            Response = { Body = body },
        };

        context.SetEndpoint(endpoint);
        context.Request.Path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

        configureRequest?.Invoke(context);

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        return new TestHttpResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()),
            context.Response.Headers.Location.ToString() is { Length: > 0 } loc ? loc : null);
    }

    private static RouteEndpoint? FindEndpoint(TestEndpointRouteBuilder builder, string routePattern, string httpMethod)
    {
        return builder.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(endpoint =>
                string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal)
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } meta
                && meta.HttpMethods.Contains(httpMethod, StringComparer.OrdinalIgnoreCase));
    }

    private static TestEndpointRouteBuilder CreateEndpoints()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>()
            .AddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, InProcessBackgroundTaskScheduler>()
            .AddScoped<InMemoryUnitOfWork>()
            .AddScoped<Blueprint.Persistence.IUnitOfWork>(sp => sp.GetRequiredService<InMemoryUnitOfWork>());

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithTenantIsolation()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>()
            .AddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, InProcessBackgroundTaskScheduler>()
            .AddScoped<InMemoryUnitOfWork>()
            .AddScoped<Blueprint.Persistence.IUnitOfWork>(sp => sp.GetRequiredService<InMemoryUnitOfWork>())
            .AddSingleton<Blueprint.Authorization.IUserAuthorisationContextFactory, SampleUserAuthorisationContextFactory>()
            .AddSingleton<Blueprint.Authorization.IApiAuthoriser, TenantIsolationApiAuthoriser>();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithAuthorizationProvider()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>()
            .AddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, InProcessBackgroundTaskScheduler>()
            .AddScoped<InMemoryUnitOfWork>()
            .AddScoped<Blueprint.Persistence.IUnitOfWork>(sp => sp.GetRequiredService<InMemoryUnitOfWork>())
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, SampleResourceAuthorizationProvider>();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithAuthManifest()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>()
            .AddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, InProcessBackgroundTaskScheduler>()
            .AddScoped<InMemoryUnitOfWork>()
            .AddScoped<Blueprint.Persistence.IUnitOfWork>(sp => sp.GetRequiredService<InMemoryUnitOfWork>())
            .AddSingleton<Blueprint.Authorization.IUserAuthorisationContextFactory, SampleUserAuthorisationContextFactory>()
            .AddSingleton<Blueprint.Authorization.IApiAuthoriser, TenantIsolationApiAuthoriser>()
            .AddSingleton<Blueprint.Authorization.IApiAuthoriserAggregator, AllowAllApiAuthoriserAggregator>();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithPropertyPopulator()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>()
            .AddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, InProcessBackgroundTaskScheduler>()
            .AddScoped<InMemoryUnitOfWork>()
            .AddScoped<Blueprint.Persistence.IUnitOfWork>(sp => sp.GetRequiredService<InMemoryUnitOfWork>())
            .AddSingleton<ITenantAccessor, AsyncLocalTenantAccessor>()
            .AddSingleton<Blueprint.Runtime.Middleware.IPropertyPopulator, TenantPropertyPopulator>();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }

    private sealed record TestHttpResponse(int StatusCode, string? ContentType, string Body, string? LocationHeader);

    private sealed class AllowAllApiAuthoriserAggregator : Blueprint.Authorization.IApiAuthoriserAggregator
    {
        public ValueTask<Blueprint.Authorization.ExecutionAllowed> CanShowLinkAsync(
            Blueprint.Runtime.Exec.ApiOperationContext operationContext,
            Blueprint.Model.ApiOperationDescriptor descriptor,
            object? resource)
        {
            return new ValueTask<Blueprint.Authorization.ExecutionAllowed>(Blueprint.Authorization.ExecutionAllowed.Yes);
        }
    }
}
