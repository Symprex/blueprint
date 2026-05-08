using System.ComponentModel.DataAnnotations;
using System.Text;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the full Blueprint HTTP stack — endpoint mapping, generated pipeline execution,
/// and HTTP result translation — works end-to-end using the generated executor and real
/// source-generated pipelines.
/// </summary>
public class HttpEndToEndIntegrationTests
{
    [Fact]
    public async Task When_GET_anonymous_with_query_param_then_returns_200_with_query_value_in_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/echo");
        endpoint.Should().NotBeNull("the echo query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?message=hello-e2e");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"hello-e2e\"");
    }

    [Fact]
    public async Task When_GET_authenticated_operation_without_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull("the protected query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task When_GET_resource_protected_operation_without_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull("the resource-protected query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task When_GET_from_resource_inherited_operation_without_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/from-resource-inherited");
        endpoint.Should().NotBeNull("the FromResource-inherited query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task When_POST_with_json_body_then_returns_200_with_bound_value_in_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/items");
        endpoint.Should().NotBeNull("the create command route should be registered");

        var bodyJson = """{"label":"e2e-test-label"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"created:e2e-test-label\"");
    }

    [Fact]
    public async Task When_POST_with_non_json_content_type_then_returns_415()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/items");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "text/plain; charset=utf-8";
            context.Request.ContentLength = 5;
            context.Request.Body = new MemoryStream("hello"u8.ToArray());
        });

        response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Unsupported Media Type");
    }

    [Fact]
    public async Task When_POST_with_malformed_json_body_then_returns_400_binding_failure()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/items");
        endpoint.Should().NotBeNull("the create command route should be registered");

        var malformedJson = "{not valid json!!!"u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = malformedJson.Length;
            context.Request.Body = new MemoryStream(malformedJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Invalid JSON body");
    }

    [Fact]
    public async Task When_POST_validation_fails_then_returns_422_with_validation_problem_details()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/validated");
        endpoint.Should().NotBeNull("the validated command route should be registered");

        // Send empty JSON object — Name is [Required] so validation should fail.
        var bodyJson = "{}"u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
    }

    [Fact]
    public async Task When_GET_resource_protected_with_authenticated_user_and_no_provider_then_returns_200()
    {
        // No IResourceAuthorizationProvider registered → permissive (authorization check skipped)
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("TestAuth"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_GET_resource_protected_with_authenticated_user_and_provider_denies_then_returns_403()
    {
        var endpoints = CreateEndpointsWithAuthorization(authorized: false);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("TestAuth"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task When_GET_resource_protected_with_authenticated_user_and_provider_allows_then_returns_200()
    {
        var endpoints = CreateEndpointsWithAuthorization(authorized: true);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("TestAuth"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_handler_throws_ApiException_with_404_then_returns_404_with_problem_details()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/not-found-exception");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.ContentType.Should().Contain("application/problem+json");
        response.Body.Should().Contain("\"status\":404");
        response.Body.Should().Contain("Resource not found");
    }

    [Fact]
    public async Task When_handler_throws_unhandled_exception_then_returns_500_with_problem_details()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/unhandled-exception");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.ContentType.Should().Contain("application/problem+json");
        response.Body.Should().Contain("\"status\":500");
    }

    [Fact]
    public async Task When_GET_with_FromHeader_property_then_header_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/header-binding");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Headers["X-Tenant-Id"] = "acme-corp";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("tenant:acme-corp");
    }

    [Fact]
    public async Task When_GET_with_FromCookie_property_then_cookie_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/cookie-binding");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Headers["Cookie"] = "session_token=abc123xyz";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("session:abc123xyz");
    }

    [Fact]
    public async Task When_void_command_then_returns_200_with_empty_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/void-command");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_no_content_command_then_returns_204()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/no-content-command");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        response.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task When_GET_with_route_parameter_then_route_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/items/{ItemId}");
        endpoint.Should().NotBeNull("the route-parameterised query should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.RouteValues = new RouteValueDictionary { ["ItemId"] = "route-42" };
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"item:route-42\"");
    }

    [Fact]
    public async Task When_GET_with_typed_route_parameter_and_valid_guid_then_route_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/typed-items/{ItemId}");
        endpoint.Should().NotBeNull("the typed-route-parameterised query should be registered");

        var id = Guid.NewGuid();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.RouteValues = new RouteValueDictionary { ["ItemId"] = id.ToString() };
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be($"\"{id}\"");
    }

    [Fact]
    public async Task When_GET_with_typed_route_parameter_and_invalid_value_then_returns_400()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/typed-items/{ItemId}");
        endpoint.Should().NotBeNull("the typed-route-parameterised query should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.RouteValues = new RouteValueDictionary { ["ItemId"] = "not-a-guid" };
        });

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Invalid route parameter");
    }

    [Fact]
    public async Task When_command_returns_CreatedResult_then_returns_201_with_json_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/widgets");
        endpoint.Should().NotBeNull("the created-result command route should be registered");

        var bodyJson = """{"name":"widget-one"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.ContentType.Should().StartWith("application/json");
        response.Body.Should().Contain("widget-one");
    }

    [Fact]
    public async Task When_query_returns_OkResult_from_union_then_returns_200_with_json_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/maybe-items/{ItemId}");
        endpoint.Should().NotBeNull("the union-result query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.RouteValues = new RouteValueDictionary { ["ItemId"] = "found-item" };
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().StartWith("application/json");
        response.Body.Should().Contain("found-item");
    }

    [Fact]
    public async Task When_query_returns_NotFoundResult_from_union_then_returns_404()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/maybe-items/{ItemId}");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.RouteValues = new RouteValueDictionary { ["ItemId"] = "missing" };
        });

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.ContentType.Should().StartWith("application/json");
    }

    [Fact]
    public async Task When_command_returns_ResourceEventResult_then_returns_201_with_envelope()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/events");
        endpoint.Should().NotBeNull("the resource-event command route should be registered");

        var bodyJson = """{"title":"test-event"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.ContentType.Should().StartWith("application/json");
        response.Body.Should().Contain("evt-001");
        response.Body.Should().Contain("res-001");
        response.Body.Should().Contain("test-event");
    }

    [Fact]
    public async Task When_handler_throws_NotFoundException_then_returns_404()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/not-found-typed");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.Body.Should().Contain("Widget not found");
    }

    [Fact]
    public async Task When_handler_throws_ConflictException_then_returns_409()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/conflict");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        response.Body.Should().Contain("Already exists");
    }

    [Fact]
    public async Task When_handler_throws_BadRequestException_then_returns_400()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/bad-request");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Body.Should().Contain("Invalid input");
    }

    [Fact]
    public async Task When_handler_returns_plain_text_result_then_returns_200_text_plain()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/plain-text");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("text/plain; charset=utf-8");
        response.Body.Should().Be("Hello, plain text!");
    }

    [Fact]
    public async Task When_handler_returns_redirect_result_then_returns_302_with_location()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/redirect");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status302Found);
        response.LocationHeader.Should().Be("https://example.com/target");
    }

    [Fact]
    public async Task When_handler_returns_file_stream_result_then_returns_200_with_binary_content()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/download");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/pdf");
        response.Body.Should().Be("fake-pdf-content");
    }

    [Fact]
    public async Task When_handler_returns_file_stream_result_with_filename_then_sets_content_disposition()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/download-named");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/octet-stream");
        response.ContentDisposition.Should().Be("attachment; filename=\"report.bin\"");
    }

    [Fact]
    public async Task When_GET_with_double_and_float_query_params_then_values_are_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/typed-query");
        endpoint.Should().NotBeNull("the typed-query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?rating=4.5&score=0.95&since=2024-06-15T10:30:00Z");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("4.5");
        response.Body.Should().Contain("0.95");
        response.Body.Should().Contain("2024-06-15");
    }

    [Fact]
    public async Task When_GET_with_nullable_double_query_param_omitted_then_null_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/typed-query");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?rating=3.14");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("3.14");
        response.Body.Should().Contain("maxPrice:null");
    }

    [Fact]
    public async Task When_GET_with_array_query_param_then_multi_value_binding_works()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/array-query");
        endpoint.Should().NotBeNull("the array-query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?tags=alpha&tags=beta&tags=gamma");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("alpha");
        response.Body.Should().Contain("beta");
        response.Body.Should().Contain("gamma");
        response.Body.Should().Contain("count:3");
    }

    [Fact]
    public async Task When_POST_with_collection_body_property_then_json_deserialization_works()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/bulk-items");
        endpoint.Should().NotBeNull("the bulk items route should be registered");

        var bodyJson = """{"names":["item-a","item-b","item-c"],"priority":5}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("item-a");
        response.Body.Should().Contain("item-b");
        response.Body.Should().Contain("item-c");
        response.Body.Should().Contain("count:3");
        response.Body.Should().Contain("priority:5");
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
        var rawPath = endpoint.RoutePattern.RawText ?? string.Empty;
        context.Request.Path = "/" + rawPath.TrimStart('/');

        configureRequest?.Invoke(context);

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        return new TestHttpResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()),
            context.Response.Headers.Location.ToString() is { Length: > 0 } loc ? loc : null,
            context.Response.Headers.ContentDisposition.ToString() is { Length: > 0 } cd ? cd : null);
    }

    private static RouteEndpoint? FindEndpoint(TestEndpointRouteBuilder builder, string routePattern)
    {
        return builder.DataSources
            .SelectMany(static ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(ep => string.Equals(ep.RoutePattern.RawText, routePattern, StringComparison.Ordinal));
    }

    private static TestEndpointRouteBuilder CreateEndpoints()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithAuthorization(bool authorized)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider>(
                new TestResourceAuthorizationProvider(authorized));

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

    private sealed record TestHttpResponse(int StatusCode, string? ContentType, string Body, string? LocationHeader, string? ContentDisposition);
}

// ─── E2E test operations ──────────────────────────────────────────────────────
// These are picked up by the IntegrationTestBlueprintApp scanner at compile time.

/// <summary>
/// Anonymous GET with a query-string parameter for end-to-end query-binding and routing validation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/echo")]
public partial class E2EEchoQuery : IQuery<string>
{
    /// <summary>The message value to echo back in the response body.</summary>
    public string? Message { get; set; }

    public static Task<string> HandleAsync(E2EEchoQuery query)
    {
        return Task.FromResult(query.Message ?? "empty");
    }
}

/// <summary>
/// Authenticated GET — exercises the authentication pipeline rejection path (401) end-to-end.
/// </summary>
[Blueprint.Model.Attributes.MustBeAuthenticated]
[Blueprint.Model.Attributes.RootLink("/e2e/protected")]
public partial class E2EProtectedQuery : IQuery<string>
{
    public static Task<string> HandleAsync(E2EProtectedQuery query)
    {
        return Task.FromResult("protected-data");
    }
}

/// <summary>
/// Resource-permissioned GET — exercises the authorization-implied identity check path (401) end-to-end.
/// Operations with <c>[Resource]</c> implicitly require authentication even without <c>[MustBeAuthenticated]</c>.
/// </summary>
[Blueprint.Model.Attributes.Resource("account", "read")]
[Blueprint.Model.Attributes.RootLink("/e2e/resource-protected")]
public partial class E2EResourceProtectedQuery : IQuery<string>
{
    public static Task<string> HandleAsync(E2EResourceProtectedQuery query)
    {
        return Task.FromResult("resource-protected-data");
    }
}

/// <summary>
/// [FromResource]-inherited permission GET — proves the pipeline generates a 401 identity check for
/// operations that inherit scope pairs from another operation via <c>[FromResource(typeof(...))]</c>.
/// </summary>
[Blueprint.Model.Attributes.FromResource(typeof(E2EResourceProtectedQuery))]
[Blueprint.Model.Attributes.RootLink("/e2e/from-resource-inherited")]
public partial class E2EFromResourceInheritedQuery : IQuery<string>
{
    public static Task<string> HandleAsync(E2EFromResourceInheritedQuery query)
    {
        return Task.FromResult("from-resource-inherited-data");
    }
}

/// <summary>
/// POST command with a body-bound property for end-to-end body-binding and routing validation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/items")]
public partial class E2ECreateCommand : ICommand<string>
{
    /// <summary>Body-bound label value reflected in the handler response.</summary>
    public string? Label { get; set; }

    public static Task<string> HandleAsync(E2ECreateCommand command)
    {
        return Task.FromResult($"created:{command.Label}");
    }
}

/// <summary>
/// POST command with a <see cref="RequiredAttribute"/> field for end-to-end validation-failure (422) testing.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/validated")]
public partial class E2EValidatedCommand : ICommand<string>
{
    /// <summary>Required name; omitting it triggers a 422 validation failure response.</summary>
    [Required]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(E2EValidatedCommand command)
    {
        return Task.FromResult($"valid:{command.Name}");
    }
}

/// <summary>
/// Test implementation of <see cref="Blueprint.Authorization.IResourceAuthorizationProvider"/>
/// that returns a fixed authorization result for integration testing.
/// </summary>
internal sealed class TestResourceAuthorizationProvider(bool authorized) : Blueprint.Authorization.IResourceAuthorizationProvider
{
    public Task<bool> IsAuthorizedAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        IReadOnlyList<Blueprint.Model.ScopePair> requiredScopePairs,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(authorized);
    }
}

/// <summary>
/// Query that throws an <see cref="ApiException"/> with HTTP 404 to verify non-500 exception translation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/not-found-exception")]
public partial class E2ENotFoundExceptionQuery : IQuery<string>
{
    /// <summary>Throws ApiException(404) to test HTTP status translation.</summary>
    /// <param name="query">The query operation instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2ENotFoundExceptionQuery query)
    {
        throw new ApiException("Resource not found", 404);
    }
}

/// <summary>
/// Query that throws a plain <see cref="InvalidOperationException"/> to verify 500 fallback.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/unhandled-exception")]
public partial class E2EUnhandledExceptionQuery : IQuery<string>
{
    /// <summary>Throws InvalidOperationException to test 500 fallback path.</summary>
    /// <param name="query">The query operation instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2EUnhandledExceptionQuery query)
    {
        throw new InvalidOperationException("Something went wrong");
    }
}

/// <summary>
/// Query with a <see cref="Blueprint.Model.Attributes.FromHeaderAttribute"/> property to verify HTTP header binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/header-binding")]
public partial class E2EHeaderBindingQuery : IQuery<string>
{
    /// <summary>Bound from X-Tenant-Id request header.</summary>
    [Blueprint.Model.Attributes.FromHeader("X-Tenant-Id")]
    public string? TenantId { get; set; }

    /// <summary>Echoes the header-bound TenantId value.</summary>
    /// <param name="query">The query with the header-bound property.</param>
    /// <returns>The TenantId value read from the header.</returns>
    public static Task<string> HandleAsync(E2EHeaderBindingQuery query)
    {
        return Task.FromResult($"tenant:{query.TenantId}");
    }
}

/// <summary>
/// Query with a <see cref="Blueprint.Model.Attributes.FromCookieAttribute"/> property to verify HTTP cookie binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/cookie-binding")]
public partial class E2ECookieBindingQuery : IQuery<string>
{
    /// <summary>Bound from session_token cookie.</summary>
    [Blueprint.Model.Attributes.FromCookie("session_token")]
    public string? SessionToken { get; set; }

    /// <summary>Echoes the cookie-bound session token value.</summary>
    /// <param name="query">The query with the cookie-bound property.</param>
    /// <returns>The SessionToken value read from the cookie.</returns>
    public static Task<string> HandleAsync(E2ECookieBindingQuery query)
    {
        return Task.FromResult($"session:{query.SessionToken}");
    }
}

/// <summary>
/// POST command that returns void (NoResultOperationResult) — verifies 200 OK with empty body.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/void-command")]
[Blueprint.Model.Attributes.AllowAnonymous]
public partial class E2EVoidCommand : ICommand<NoResultOperationResult>
{
    /// <summary>Does nothing — fire-and-forget style command.</summary>
    /// <returns>NoResultOperationResult to signal void success.</returns>
    public static Task<NoResultOperationResult> HandleAsync()
    {
        return Task.FromResult(new NoResultOperationResult());
    }
}

/// <summary>
/// Command that returns HttpNoContentResult — verifies 204 No Content.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/no-content-command")]
[Blueprint.Model.Attributes.AllowAnonymous]
public partial class E2ENoContentCommand : ICommand<HttpNoContentResult>
{
    /// <summary>Returns 204 No Content — typical DELETE response.</summary>
    /// <returns>HttpNoContentResult indicating successful deletion.</returns>
    public static Task<HttpNoContentResult> HandleAsync()
    {
        return Task.FromResult(new HttpNoContentResult());
    }
}

/// <summary>
/// Query with a route parameter to verify route-value binding end-to-end.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/items/{ItemId}")]
public partial class E2ERouteParamQuery : IQuery<string>
{
    /// <summary>Bound from the {ItemId} route segment.</summary>
    public string? ItemId { get; set; }

    /// <summary>Echoes the route-bound ItemId value.</summary>
    /// <param name="query">The query with the route-bound property.</param>
    /// <returns>A string echoing the ItemId value.</returns>
    public static Task<string> HandleAsync(E2ERouteParamQuery query)
    {
        return Task.FromResult($"item:{query.ItemId}");
    }
}

/// <summary>
/// Query with a typed (Guid) route parameter to verify route-value TryParse binding and 400 on invalid values.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/typed-items/{ItemId}")]
public partial class E2ETypedRouteParamQuery : IQuery<Guid>
{
    /// <summary>Bound from the {ItemId} route segment as a <see cref="Guid"/>.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Echoes the parsed Guid back.</summary>
    /// <param name="query">The query with the typed route parameter.</param>
    /// <returns>The parsed Guid value.</returns>
    public static Task<Guid> HandleAsync(E2ETypedRouteParamQuery query)
    {
        return Task.FromResult(query.ItemId);
    }
}

/// <summary>
/// Command that returns <see cref="CreatedResult{T}"/> to verify 201 Created response translation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/widgets")]
public partial class E2ECreatedCommand : ICommand<CreatedResult<E2EWidgetResponse>>
{
    /// <summary>Body-bound widget name.</summary>
    public string? Name { get; set; }

    /// <summary>Returns a 201 Created result wrapping the new widget.</summary>
    /// <param name="command">The command with the body-bound name.</param>
    /// <returns>A CreatedResult wrapping the new widget.</returns>
    public static Task<CreatedResult<E2EWidgetResponse>> HandleAsync(E2ECreatedCommand command)
    {
        return Task.FromResult(new CreatedResult<E2EWidgetResponse>(
            new E2EWidgetResponse { Name = command.Name ?? "unnamed" }));
    }
}

/// <summary>
/// Simple response DTO for the created-result E2E test.
/// </summary>
public class E2EWidgetResponse
{
    /// <summary>The widget name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Query returning a discriminated union — found items return <see cref="OkResult{T}"/>,
/// missing items return <see cref="NotFoundResult{T}"/>.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/maybe-items/{ItemId}")]
public partial class E2EMaybeItemQuery : IQuery<Results<OkResult<E2EWidgetResponse>, NotFoundResult<E2EWidgetResponse>>>
{
    /// <summary>Bound from the {ItemId} route segment; "missing" triggers the 404 path.</summary>
    public string? ItemId { get; set; }

    /// <summary>Returns Ok for any ID except "missing".</summary>
    /// <param name="query">The query with the route-bound ItemId.</param>
    /// <returns>OkResult for found items, NotFoundResult for "missing".</returns>
    public static Task<Results<OkResult<E2EWidgetResponse>, NotFoundResult<E2EWidgetResponse>>> HandleAsync(
        E2EMaybeItemQuery query)
    {
        if (query.ItemId == "missing")
        {
            return Task.FromResult<Results<OkResult<E2EWidgetResponse>, NotFoundResult<E2EWidgetResponse>>>(
                new NotFoundResult<E2EWidgetResponse>(new E2EWidgetResponse { Name = "not-found" }));
        }

        return Task.FromResult<Results<OkResult<E2EWidgetResponse>, NotFoundResult<E2EWidgetResponse>>>(
            new OkResult<E2EWidgetResponse>(new E2EWidgetResponse { Name = query.ItemId ?? "unknown" }));
    }
}

/// <summary>
/// Command that returns <see cref="ResourceEventResult{T}"/> to verify 201 with envelope.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/events")]
public partial class E2EResourceEventCommand : ICommand<ResourceEventResult<E2EWidgetResponse>>
{
    /// <summary>Body-bound event title.</summary>
    public string? Title { get; set; }

    /// <summary>Returns a resource event result with a stable event/resource ID.</summary>
    /// <param name="command">The command with the body-bound title.</param>
    /// <returns>A ResourceEventResult wrapping the widget response.</returns>
    public static Task<ResourceEventResult<E2EWidgetResponse>> HandleAsync(E2EResourceEventCommand command)
    {
        return Task.FromResult(new ResourceEventResult<E2EWidgetResponse>(
            ChangeType.Created,
            "evt-001",
            "res-001",
            new E2EWidgetResponse { Name = command.Title ?? "untitled" }));
    }
}

/// <summary>
/// Query that throws <see cref="NotFoundException"/> to verify the typed convenience exception returns 404.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/not-found-typed")]
public partial class E2ENotFoundTypedQuery : IQuery<string>
{
    /// <summary>Always throws NotFoundException.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2ENotFoundTypedQuery query)
    {
        throw new NotFoundException("Widget not found");
    }
}

/// <summary>
/// Query that throws <see cref="ConflictException"/> to verify 409 response.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/conflict")]
public partial class E2EConflictQuery : IQuery<string>
{
    /// <summary>Always throws ConflictException.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2EConflictQuery query)
    {
        throw new ConflictException("Already exists");
    }
}

/// <summary>
/// Query that throws <see cref="BadRequestException"/> to verify 400 response.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/bad-request")]
public partial class E2EBadRequestQuery : IQuery<string>
{
    /// <summary>Always throws BadRequestException.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2EBadRequestQuery query)
    {
        throw new BadRequestException("Invalid input");
    }
}

/// <summary>E2E operation returning a plain-text response (text/plain instead of JSON).</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/plain-text")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EPlainTextQuery : IQuery<PlainTextResult>
{
    /// <summary>Returns a plain-text response.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>A plain-text result.</returns>
    public static Task<PlainTextResult> HandleAsync(E2EPlainTextQuery query)
    {
        return Task.FromResult(new PlainTextResult("Hello, plain text!"));
    }
}

/// <summary>E2E operation returning an HTTP redirect response.</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/redirect")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2ERedirectQuery : IQuery<Blueprint.Runtime.Http.RedirectResult>
{
    /// <summary>Returns a redirect to an external URL.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>A redirect result.</returns>
    public static Task<Blueprint.Runtime.Http.RedirectResult> HandleAsync(E2ERedirectQuery query)
    {
        return Task.FromResult(new Blueprint.Runtime.Http.RedirectResult("https://example.com/target"));
    }
}

/// <summary>E2E operation returning a file download without a file name (inline).</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/download")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EFileDownloadQuery : IQuery<FileStreamResult>
{
    /// <summary>Returns a binary PDF stream.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>A file stream result with PDF content type.</returns>
    public static Task<FileStreamResult> HandleAsync(E2EFileDownloadQuery query)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-pdf-content"));
        return Task.FromResult(new FileStreamResult(stream, "application/pdf"));
    }
}

/// <summary>E2E operation returning a file download with a file name (attachment).</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/download-named")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2ENamedFileDownloadQuery : IQuery<FileStreamResult>
{
    /// <summary>Returns a binary stream with a Content-Disposition attachment header.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>A file stream result with file name.</returns>
    public static Task<FileStreamResult> HandleAsync(E2ENamedFileDownloadQuery query)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("binary-data"));
        return Task.FromResult(new FileStreamResult(stream, "application/octet-stream", "report.bin"));
    }
}

/// <summary>
/// Anonymous GET with double, float, nullable double, and DateTimeOffset query params
/// for end-to-end type-binding validation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/typed-query")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2ETypedQueryParamsQuery : IQuery<string>
{
    /// <summary>A double-precision rating value.</summary>
    public double Rating { get; set; }

    /// <summary>A single-precision score value.</summary>
    public float Score { get; set; }

    /// <summary>A nullable double for optional max price filter.</summary>
    public double? MaxPrice { get; set; }

    /// <summary>An optional date-time-offset filter.</summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>Echoes back the parsed values for assertion.</summary>
    /// <param name="query">The typed query instance.</param>
    /// <returns>A formatted string of the parsed values.</returns>
    public static Task<string> HandleAsync(E2ETypedQueryParamsQuery query)
    {
        var maxPriceStr = query.MaxPrice.HasValue ? query.MaxPrice.Value.ToString("G") : "null";
        var sinceStr = query.Since.HasValue ? query.Since.Value.ToString("o") : "null";
        return Task.FromResult($"rating:{query.Rating:G}|score:{query.Score:G}|maxPrice:{maxPriceStr}|since:{sinceStr}");
    }
}

/// <summary>
/// Anonymous GET with a <c>string[]</c> query parameter for end-to-end multi-value query-string binding.
/// Proves that <c>?tags=a&amp;tags=b&amp;tags=c</c> is correctly bound to a <c>string[]</c> property.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/array-query")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EArrayQueryParamsQuery : IQuery<string>
{
    /// <summary>Multi-value tag filter.</summary>
    public string[]? Tags { get; set; }

    /// <summary>Echoes back the parsed array values for assertion.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A formatted string showing the bound array contents.</returns>
    public static Task<string> HandleAsync(E2EArrayQueryParamsQuery query)
    {
        var tags = query.Tags ?? [];
        return Task.FromResult($"count:{tags.Length}|values:{string.Join(",", tags)}");
    }
}

/// <summary>
/// Anonymous POST with a body containing a collection property (<c>string[]</c>) and a scalar property.
/// Proves that JSON deserialization handles array properties in command bodies end-to-end.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/bulk-items")]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EBulkItemsCommand : ICommand<string>
{
    /// <summary>A collection of item names to create.</summary>
    public string[]? Names { get; set; }

    /// <summary>A scalar priority value alongside the collection.</summary>
    public int Priority { get; set; }

    /// <summary>Echoes back the parsed body values for assertion.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A formatted string showing the bound collection and scalar values.</returns>
    public static Task<string> HandleAsync(E2EBulkItemsCommand command)
    {
        var names = command.Names ?? [];
        return Task.FromResult($"count:{names.Length}|items:{string.Join(",", names)}|priority:{command.Priority}");
    }
}
