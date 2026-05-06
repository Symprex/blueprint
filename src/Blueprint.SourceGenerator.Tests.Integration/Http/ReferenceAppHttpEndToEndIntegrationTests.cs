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

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
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
    public async Task When_archive_template_with_auth_then_returns_201_key_only()
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

        // KeyOnlyResourceEventResult produces 201 Created with no body.
        response.StatusCode.Should().Be(StatusCodes.Status201Created);
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

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.Body.Should().Contain("template.published");
    }

    [Fact]
    public async Task When_publish_template_already_published_then_returns_409_conflict()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}/publish", "POST");

        // First publish succeeds (201 Created)
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

        firstResponse.StatusCode.Should().Be(StatusCodes.Status201Created);

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
            Encoding.UTF8.GetString(body.ToArray()));
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
            .AddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, InProcessBackgroundTaskScheduler>();

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
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, SampleResourceAuthorizationProvider>();

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

    private sealed record TestHttpResponse(int StatusCode, string? ContentType, string Body);
}
