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

        // Legacy's anonymous refusal, UnauthorizedException("Access denied. Anonymous access is not allowed.") (Signature365
        // 8b40ecc961, Authorisation/AuthorisationMiddlewareBuilder.cs:16, :75-79; Errors/UnauthorizedException.cs:15-18).
        response.Body.Should().Be("""{"type":"security_failure","title":"Access denied. Anonymous access is not allowed.","status":401}""");
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
    public async Task When_POST_with_non_json_content_type_then_the_body_is_ignored_and_it_runs()
    {
        // R116: legacy read the body of every non-GET method through whichever IBodyParser.CanRead matched the content
        // type, and silently left the operation unpopulated when none did, for POST/PUT/PATCH alike, not only
        // DELETE/HEAD/OPTIONS (R108) (Signature365 8b40ecc961, HttpBodyMessagePopulationSource.cs:80-86). No 415 anywhere
        // in that path.
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

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"created:\"");
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
    public async Task When_GET_resource_protected_with_authenticated_user_and_provider_permits_then_returns_200()
    {
        // CreateEndpoints registers PermitAllResourceAuthorizationProvider, as a missing provider no longer permits (#41)
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
        response.Body.Should().Be(
            """{"type":"unauthorized","title":"denied","status":403}""",
            "a provider's denial is written as an authoriser's is, titled with its message (Symprex/blueprint#77)");
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
    public async Task When_GET_resource_protected_with_ownership_provider_matching_tenant_then_returns_200()
    {
        var endpoints = CreateEndpointsWithOwnershipAuthorization();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/tenant-resource/{TenantId}");
        endpoint.Should().NotBeNull("the tenant-resource query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            // Set authenticated user with tenant_id claim matching the route
            var identity = new System.Security.Claims.ClaimsIdentity("TestAuth");
            identity.AddClaim(new System.Security.Claims.Claim("scope", "tenant:read"));
            identity.AddClaim(new System.Security.Claims.Claim("tenant_id", "tenant-abc"));
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);

            // Route value provides the TenantId to the operation
            context.Request.RouteValues["TenantId"] = "tenant-abc";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"tenant-data:tenant-abc\"");
    }

    [Fact]
    public async Task When_GET_resource_protected_with_ownership_provider_mismatched_tenant_then_returns_403()
    {
        var endpoints = CreateEndpointsWithOwnershipAuthorization();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/tenant-resource/{TenantId}");
        endpoint.Should().NotBeNull("the tenant-resource query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            // Set authenticated user with tenant_id claim that does NOT match the route
            var identity = new System.Security.Claims.ClaimsIdentity("TestAuth");
            identity.AddClaim(new System.Security.Claims.Claim("scope", "tenant:read"));
            identity.AddClaim(new System.Security.Claims.Claim("tenant_id", "tenant-abc"));
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);

            // Route value provides a DIFFERENT TenantId
            context.Request.RouteValues["TenantId"] = "tenant-xyz";
        });

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task When_property_populator_registered_and_user_authenticated_then_populates_TenantId()
    {
        var endpoints = CreateEndpointsWithPropertyPopulator();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/populated-tenant");
        endpoint.Should().NotBeNull("the populated-tenant query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            var identity = new System.Security.Claims.ClaimsIdentity("TestAuth");
            identity.AddClaim(new System.Security.Claims.Claim("tenant_id", "pop-tenant-123"));
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"tenant:pop-tenant-123\"");
    }

    [Fact]
    public async Task When_property_populator_registered_and_user_anonymous_then_TenantId_remains_null()
    {
        var endpoints = CreateEndpointsWithPropertyPopulator();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/populated-tenant");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"tenant:\"");
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
    public async Task When_accepted_command_then_returns_202()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/accepted-command");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
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
        response.Body.Should().Contain("test-event");
        response.Body.Should().Contain("\"$object\":\"event\"");
        response.Body.Should().Contain("\"changeType\":\"Created\"");
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

        // No charset: legacy wrote ContentType exactly (Signature365 8b40ecc961, Blueprint.Http/PlainTextResult.cs:57).
        response.ContentType.Should().Be("text/plain");
        response.Body.Should().Be("Hello, plain text!");
    }

    [Fact]
    public async Task When_handler_returns_plain_text_with_custom_status_code_then_respects_status()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/plain-text-not-found");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.ContentType.Should().Be("text/plain");
        response.Body.Should().Be("Resource not found");
    }

    [Fact]
    public async Task When_handler_returns_plain_text_with_custom_content_type_then_respects_content_type()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/vcard");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("text/vcard");
        response.Body.Should().Be("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:John Doe\r\nEND:VCARD");
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

    [Fact]
    public async Task When_PATCH_with_json_body_then_returns_200_with_bound_value()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/templates/{Id}");
        endpoint.Should().NotBeNull("the PATCH template route should be registered");

        var bodyJson = """{"description":"updated desc"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = "PATCH";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.RouteValues["id"] = "abc-123";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("abc-123");
        response.Body.Should().Contain("updated desc");
    }

    [Fact]
    public async Task When_PUT_with_json_body_then_returns_200_with_bound_value()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/settings/{Id}");
        endpoint.Should().NotBeNull("the PUT settings route should be registered");

        var bodyJson = """{"value":"new-setting-value"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = "PUT";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.Request.RouteValues["id"] = "setting-42";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("setting-42");
        response.Body.Should().Contain("new-setting-value");
    }

    [Fact]
    public async Task When_DELETE_then_returns_204_no_content()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/delete-items/{Id}");
        endpoint.Should().NotBeNull("the DELETE items route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = "DELETE";
            context.Request.RouteValues["id"] = "item-99";
        });

        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task When_handler_returns_HttpResult_then_status_and_custom_headers_are_applied()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/http-result");
        endpoint.Should().NotBeNull("the HttpResult command route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        response.Body.Should().BeEmpty();
        response.CustomHeaders.Should().ContainKey("X-Custom-E2E");
        response.CustomHeaders["X-Custom-E2E"].Should().Be("test-value");
    }

    [Fact]
    public async Task When_handler_returns_ProxyResult_then_proxies_status_headers_and_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/proxy");
        endpoint.Should().NotBeNull("the proxy query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("\"analytics\"");
        response.CustomHeaders.Should().ContainKey("X-Downstream-Id");
        response.CustomHeaders["X-Downstream-Id"].Should().Be("cubejs-123");
        response.CustomHeaders.Should().ContainKey("X-Request-ID");
        response.CustomHeaders["X-Request-ID"].Should().Be("req-456");
    }

    [Fact]
    public async Task When_conditional_RequiredIf_is_satisfied_and_field_missing_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/conditional-validation");
        endpoint.Should().NotBeNull("the conditional validation route should be registered");

        // Mode is "export" which triggers RequiredIf on OutputPath, but OutputPath is not provided
        var bodyJson = """{"mode":"export"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("OutputPath");
    }

    [Fact]
    public async Task When_conditional_RequiredIf_is_satisfied_and_field_present_then_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/conditional-validation");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"mode":"export","outputPath":"/tmp/export.csv"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("export");
        response.Body.Should().Contain("/tmp/export.csv");
    }

    [Fact]
    public async Task When_conditional_RequiredIf_not_triggered_then_field_not_required()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/conditional-validation");
        endpoint.Should().NotBeNull();

        // Mode is "preview" (not "export"), so OutputPath should NOT be required
        var bodyJson = """{"mode":"preview"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("preview");
    }

    [Fact]
    public async Task When_GET_with_enum_query_param_then_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/enum-query");
        endpoint.Should().NotBeNull("the enum query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?status=Active&priority=2");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("Active");
        response.Body.Should().Contain("2");
    }

    [Fact]
    public async Task When_command_returns_AcceptedResult_then_returns_202_with_json_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/accepted-job")!;

        var bodyJson = """{"jobName":"import-users"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        response.ContentType.Should().StartWith("application/json");
        response.Body.Should().Contain("import-users");
    }

    [Fact]
    public async Task When_command_returns_bodyless_StatusCodeResult_Accepted_then_returns_202_with_no_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/bodyless-accepted")!;

        var bodyJson = """{"reason":"background-sync"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        response.Body.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task When_command_returns_bodyless_StatusCodeResult_NoContent_then_returns_204_with_no_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/bodyless-nocontent")!;

        var bodyJson = """{"reason":"cleanup"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        response.Body.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task When_command_returns_ConflictResult_then_returns_409_with_json_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/conflict-check/{Name}")!;

        var bodyJson = "{}"u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
            ctx.Request.RouteValues = new RouteValueDictionary { { "Name", "existing-item" } };
        });

        response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        response.ContentType.Should().StartWith("application/json");
        response.Body.Should().Contain("existing-item");
    }

    [Fact]
    public async Task When_GET_with_datetime_query_param_then_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/datetime-binding")!;

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.QueryString = new QueryString("?Since=2024-06-15T10:30:00Z&Until=2024-12-31");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("2024-06-15");
        response.Body.Should().Contain("2024-12-31");
    }

    [Fact]
    public async Task When_GET_with_decimal_query_param_then_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/decimal-binding")!;

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.QueryString = new QueryString("?Amount=1234.56&TaxRate=0.175");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("1234.56");
        response.Body.Should().Contain("0.175");
    }

    [Fact]
    public async Task When_GET_with_nullable_int_query_param_omitted_then_null_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/nullable-int")!;

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.QueryString = new QueryString("?Name=test");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("name:test|page:null|size:null");
    }

    [Fact]
    public async Task When_GET_with_nullable_int_query_param_provided_then_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/nullable-int")!;

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.QueryString = new QueryString("?Name=test&Page=3&PageSize=25");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("name:test|page:3|size:25");
    }

    [Fact]
    public async Task When_GET_with_bool_query_param_then_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/bool-binding")!;

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.QueryString = new QueryString("?IncludeArchived=true&Verbose=false");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("archived:True|verbose:False");
    }

    [Fact]
    public async Task When_POST_with_multiple_validation_errors_then_returns_422_with_all_errors()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/multi-validation")!;

        var bodyJson = """{"email":"","age":-1}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.Body.Should().Contain("Name");
        response.Body.Should().Contain("Email");
    }

    [Fact]
    public async Task When_command_returns_KeyOnlyResourceEventResult_created_then_returns_201_no_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/key-only-event")!;

        var bodyJson = """{"resourceId":"res-123"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status201Created);
    }

    [Fact]
    public async Task When_GET_with_invalid_enum_query_param_then_returns_400()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/enum-query");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?status=InvalidValue&priority=1");
        });

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_command_returns_ResourceEventResult_updated_then_returns_200_with_envelope()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/events-update");
        endpoint.Should().NotBeNull("the resource-event-update command route should be registered");

        var bodyJson = """{"title":"updated-widget"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().StartWith("application/json");
        response.Body.Should().Contain("evt-update-001");
        response.Body.Should().Contain("updated-widget");
        response.Body.Should().Contain("\"$object\":\"event\"");
        response.Body.Should().Contain("\"changeType\":\"Updated\"");
    }

    [Fact]
    public async Task When_command_returns_ResourceEventResult_deleted_then_returns_204_no_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/events-delete");
        endpoint.Should().NotBeNull("the resource-event-delete command route should be registered");

        var bodyJson = """{"resourceId":"del-001"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        response.Body.Should().BeEmpty();
        response.ContentType.Should().BeNull();
    }

    [Fact]
    public async Task When_command_returns_KeyOnlyResourceEventResult_updated_then_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/key-only-event-update")!;

        var bodyJson = """{"resourceId":"res-upd-001"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_command_returns_KeyOnlyResourceEventResult_deleted_then_returns_204()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/e2e/key-only-event-delete")!;

        var bodyJson = """{"resourceId":"res-del-001"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            ctx.Request.ContentType = "application/json; charset=utf-8";
            ctx.Request.ContentLength = bodyJson.Length;
            ctx.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task When_handler_throws_ApiException_with_503_then_returns_503_with_problem_details()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/service-unavailable");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(503);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("\"status\":503");
        response.Body.Should().Contain("Downstream service unavailable");
    }

    [Fact]
    public async Task When_GET_with_multiple_route_parameters_then_all_are_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/parents/{ParentId}/children/{ChildId}");
        endpoint.Should().NotBeNull("the multi-route-param endpoint should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["ParentId"] = "parent-42",
                ["ChildId"] = "child-7",
            };
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("parent-42");
        response.Body.Should().Contain("child-7");
    }

    [Fact]
    public async Task When_POST_with_empty_body_on_required_fields_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/key-only-event");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = 0;
            context.Request.Body = new MemoryStream(Array.Empty<byte>());
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_POST_with_wrong_content_type_then_the_body_is_ignored_and_required_fields_fail_validation()
    {
        // R116: the same silent-ignore as the items endpoint above; this operation has required fields the body would
        // have populated, so ignoring it fails validation exactly as an empty body does
        // (When_POST_with_empty_body_on_required_fields_then_returns_422, same route), not a 415.
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/key-only-event");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/xml";
            context.Request.ContentLength = 10;
            context.Request.Body = new MemoryStream("<r/>"u8.ToArray());
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_GET_with_catch_all_path_parameter_then_routes_and_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/folder-link-test/{**FolderPath}");
        endpoint.Should().NotBeNull("catch-all folder route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Path = "/api/folder-link-test/UK/Marketing/Assets";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["FolderPath"] = "UK/Marketing/Assets",
            };
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("UK/Marketing/Assets");
    }

    [Fact]
    public async Task When_GET_with_IParsable_route_parameter_then_routes_and_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/population-test/items/{ItemId}");
        endpoint.Should().NotBeNull("IParsable route parameter endpoint should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Path = "/api/population-test/items/my-item-99";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["ItemId"] = "my-item-99",
            };
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("my-item-99");
    }

    [Fact]
    public async Task When_GET_with_strict_IParsable_route_parameter_and_invalid_value_then_returns_400()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/population-test/strict-items/{ItemId}");
        endpoint.Should().NotBeNull("strict IParsable route parameter endpoint should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Path = "/api/population-test/strict-items/NOT_A_NUMBER";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["ItemId"] = "NOT_A_NUMBER",
            };
        });

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_GET_with_IParsable_query_parameter_then_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/population-test/search-items");
        endpoint.Should().NotBeNull("IParsable query parameter endpoint should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Path = "/api/population-test/search-items";
            context.Request.QueryString = new QueryString("?FilterId=typed-id-99");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("typed-id-99");
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

        var customHeaders = context.Response.Headers
            .Where(static h => h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static h => h.Key, static h => h.Value.ToString());

        return new TestHttpResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()),
            context.Response.Headers.Location.ToString() is { Length: > 0 } loc ? loc : null,
            context.Response.Headers.ContentDisposition.ToString() is { Length: > 0 } cd ? cd : null,
            customHeaders);
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
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, PermitAllResourceAuthorizationProvider>();

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

    private static TestEndpointRouteBuilder CreateEndpointsWithOwnershipAuthorization()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider>(
                new OwnershipAwareAuthorizationProvider());

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithPropertyPopulator()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, PermitAllResourceAuthorizationProvider>()
            .AddSingleton<Blueprint.Runtime.Middleware.IPropertyPopulator>(
                new TestTenantPropertyPopulator());

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

    private sealed record TestHttpResponse(int StatusCode, string? ContentType, string Body, string? LocationHeader, string? ContentDisposition, Dictionary<string, string> CustomHeaders);
}

// ─── E2E test operations ──────────────────────────────────────────────────────
// These are picked up by the IntegrationTestBlueprintApp scanner at compile time.

/// <summary>
/// Anonymous GET with a query-string parameter for end-to-end query-binding and routing validation.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
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
/// Authenticated query whose handler takes <see cref="Blueprint.Authorization.IUserAuthorisationContext"/> as a method
/// parameter, proving context-property injection (not DI) works end-to-end.
/// </summary>
[Blueprint.Model.Attributes.MustBeAuthenticated]
[Blueprint.Model.Attributes.RootLink("/e2e/identity-echo")]
public partial class E2EIdentityEchoQuery : IQuery<string>
{
    public static Task<string> HandleAsync(E2EIdentityEchoQuery query, Blueprint.Authorization.IUserAuthorisationContext identity)
    {
        return Task.FromResult($"user:{identity.Id}|account:{identity.AccountId}");
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
    public ValueTask<Blueprint.Authorization.ExecutionAllowed> AuthorizeAsync(
        Blueprint.Runtime.Exec.ApiOperationContext operationContext,
        System.Security.Claims.ClaimsPrincipal principal,
        Blueprint.Authorization.ResourceAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<Blueprint.Authorization.ExecutionAllowed>(authorized
            ? Blueprint.Authorization.ExecutionAllowed.Yes
            : OwnershipAwareAuthorizationProvider.Denied);
    }
}

/// <summary>
/// Ownership-aware authorization provider that checks both scope pair claims AND verifies
/// the caller's tenant_id claim matches the operation's TenantId property. This proves
/// the <see cref="Blueprint.Runtime.Exec.ApiOperationContext"/> parameter enables resource
/// ownership patterns analogous to legacy Blueprint's resource key templates.
/// </summary>
internal sealed class OwnershipAwareAuthorizationProvider : Blueprint.Authorization.IResourceAuthorizationProvider
{
    internal static readonly Blueprint.Authorization.ExecutionAllowed Denied = Blueprint.Authorization.ExecutionAllowed.No(
        "denied by test provider",
        "denied",
        Blueprint.Authorization.ExecutionAllowedFailureType.Authorisation);

    public ValueTask<Blueprint.Authorization.ExecutionAllowed> AuthorizeAsync(
        Blueprint.Runtime.Exec.ApiOperationContext operationContext,
        System.Security.Claims.ClaimsPrincipal principal,
        Blueprint.Authorization.ResourceAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        // Check scope pairs (any-of)
        var hasScopePair = false;

        foreach (var scopePair in request.ScopePairs)
        {
            if (principal.HasClaim("scope", $"{scopePair.Noun}:{scopePair.Verb}"))
            {
                hasScopePair = true;
                break;
            }
        }

        if (!hasScopePair)
        {
            return new ValueTask<Blueprint.Authorization.ExecutionAllowed>(Denied);
        }

        // Ownership check: verify caller's tenant_id matches operation's TenantId property
        var tenantProperty = operationContext.Operation.GetType().GetProperty("TenantId");

        if (tenantProperty != null)
        {
            var operationTenantId = tenantProperty.GetValue(operationContext.Operation)?.ToString();
            var callerTenantId = principal.FindFirst("tenant_id")?.Value;

            if (operationTenantId != null && callerTenantId != null &&
                !string.Equals(operationTenantId, callerTenantId, StringComparison.Ordinal))
            {
                return new ValueTask<Blueprint.Authorization.ExecutionAllowed>(Denied);
            }
        }

        return new ValueTask<Blueprint.Authorization.ExecutionAllowed>(Blueprint.Authorization.ExecutionAllowed.Yes);
    }
}

/// <summary>
/// Tenant-scoped resource-protected query that carries a <c>TenantId</c> property bound from
/// the route. Used to prove that <see cref="Blueprint.Authorization.IResourceAuthorizationProvider"/>
/// can inspect operation properties via <see cref="Blueprint.Runtime.Exec.ApiOperationContext"/>
/// for ownership-based authorization (analogous to legacy Blueprint's resource key template
/// pattern: <c>[Resource("noun", "verb", "/Tenants/{Auth.AccountId}")]</c>).
/// </summary>
[Blueprint.Model.Attributes.Resource("tenant", "read")]
[Blueprint.Model.Attributes.RootLink("/e2e/tenant-resource/{TenantId}")]
public partial class E2ETenantResourceQuery : IQuery<string>
{
    /// <summary>Tenant identifier bound from the route, used in ownership authorization checks.</summary>
    public string TenantId { get; set; } = string.Empty;

    public static Task<string> HandleAsync(E2ETenantResourceQuery query)
    {
        return Task.FromResult($"tenant-data:{query.TenantId}");
    }
}

/// <summary>
/// Query that throws an <see cref="ApiException"/> with HTTP 404 to verify non-500 exception translation.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
        return Task.FromResult(NoResultOperationResult.Instance);
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
/// Command that returns HttpAcceptedResult — verifies 202 Accepted with no body.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/accepted-command")]
[Blueprint.Model.Attributes.AllowAnonymous]
public partial class E2EAcceptedCommand : ICommand<HttpAcceptedResult>
{
    /// <summary>Returns 202 Accepted — typical async fire-and-forget response.</summary>
    /// <returns>HttpAcceptedResult indicating the command was accepted for processing.</returns>
    public static Task<HttpAcceptedResult> HandleAsync()
    {
        return Task.FromResult(new HttpAcceptedResult());
    }
}

/// <summary>
/// Query with a route parameter to verify route-value binding end-to-end.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/e2e/items/{ItemId}")]
public partial class E2ERouteParamQuery : IQuery<string>
{
    /// <summary>Bound from the {ItemId} route segment.</summary>
    public string ItemId { get; set; } = string.Empty;

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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/e2e/maybe-items/{ItemId}")]
public partial class E2EMaybeItemQuery : IQuery<Results<OkResult<E2EWidgetResponse>, NotFoundResult<E2EWidgetResponse>>>
{
    /// <summary>Bound from the {ItemId} route segment; "missing" triggers the 404 path.</summary>
    public string ItemId { get; set; } = string.Empty;

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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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
[Blueprint.Model.Attributes.AllowAnonymous]
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

/// <summary>E2E operation returning a plain-text response with a 404 status code.</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/plain-text-not-found")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EPlainTextNotFoundQuery : IQuery<PlainTextResult>
{
    /// <summary>Returns a plain-text response with a 404 status code.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>A not-found plain-text result.</returns>
    public static Task<PlainTextResult> HandleAsync(E2EPlainTextNotFoundQuery query)
    {
        return Task.FromResult(new PlainTextResult("Resource not found", System.Net.HttpStatusCode.NotFound));
    }
}

/// <summary>E2E operation returning a vCard-typed plain-text response (text/vcard content type).</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/vcard")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EVCardQuery : IQuery<PlainTextResult>
{
    /// <summary>Returns a vCard response with custom content type.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>A plain-text result with text/vcard content type.</returns>
    public static Task<PlainTextResult> HandleAsync(E2EVCardQuery query)
    {
        return Task.FromResult(new PlainTextResult("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:John Doe\r\nEND:VCARD")
        {
            ContentType = "text/vcard",
        });
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

/// <summary>
/// Anonymous PATCH with a route parameter and JSON body for end-to-end partial-update binding.
/// Proves that <c>[HttpPatch]</c> operations with mixed route and body parameters work correctly.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/templates/{Id}")]
[Blueprint.Model.Attributes.HttpPatch]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EPatchTemplateCommand : ICommand<string>
{
    /// <summary>The template identifier from the route.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>An optional description update from the body.</summary>
    public string? Description { get; set; }

    /// <summary>Echoes back the bound route and body values.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A formatted string showing the bound values.</returns>
    public static Task<string> HandleAsync(E2EPatchTemplateCommand command)
    {
        return Task.FromResult($"id:{command.Id}|description:{command.Description}");
    }
}

/// <summary>
/// Anonymous PUT with a route parameter and JSON body for end-to-end replacement-update binding.
/// Proves that <c>[HttpPut]</c> operations with mixed route and body parameters work correctly.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/settings/{Id}")]
[Blueprint.Model.Attributes.HttpPut]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EPutSettingCommand : ICommand<string>
{
    /// <summary>The setting identifier from the route.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The setting value from the body.</summary>
    public string? Value { get; set; }

    /// <summary>Echoes back the bound route and body values.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A formatted string showing the bound values.</returns>
    public static Task<string> HandleAsync(E2EPutSettingCommand command)
    {
        return Task.FromResult($"id:{command.Id}|value:{command.Value}");
    }
}

/// <summary>
/// Anonymous DELETE with a route parameter for end-to-end delete operation binding.
/// Proves that <c>[HttpDelete]</c> operations return <see cref="HttpNoContentResult"/> (204).
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/delete-items/{Id}")]
[Blueprint.Model.Attributes.HttpDelete]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EDeleteItemCommand : ICommand<HttpNoContentResult>
{
    /// <summary>The item identifier to delete.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Returns 204 No Content after deletion.</summary>
    /// <returns>An <see cref="HttpNoContentResult"/>.</returns>
    public static Task<HttpNoContentResult> HandleAsync()
    {
        return Task.FromResult(new HttpNoContentResult());
    }
}

/// <summary>
/// Anonymous POST with <see cref="Blueprint.Validation.RequiredIfAttribute"/> for conditional
/// validation end-to-end testing. When <c>Mode</c> is <c>"export"</c>, <c>OutputPath</c> is required.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/conditional-validation")]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EConditionalValidationCommand : ICommand<string>
{
    /// <summary>The operation mode — "preview" or "export".</summary>
    [Required]
    public string Mode { get; set; } = string.Empty;

    /// <summary>Output path, required only when Mode is "export".</summary>
    [Blueprint.Validation.RequiredIf(nameof(Mode), "export")]
    public string? OutputPath { get; set; }

    /// <summary>Echoes back the bound values.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A formatted string showing the bound values.</returns>
    public static Task<string> HandleAsync(E2EConditionalValidationCommand command)
    {
        return Task.FromResult($"mode:{command.Mode}|outputPath:{command.OutputPath ?? "null"}");
    }
}

/// <summary>
/// Anonymous GET with enum and int query parameters for end-to-end enum binding.
/// Proves that enum-typed query parameters are correctly parsed via TryParse.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/enum-query")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EEnumQueryParamsQuery : IQuery<string>
{
    /// <summary>An enum-typed query parameter.</summary>
    public E2EItemStatus Status { get; set; }

    /// <summary>An integer priority alongside the enum.</summary>
    public int Priority { get; set; }

    /// <summary>Echoes back the parsed enum and int values.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A formatted string showing the bound values.</returns>
    public static Task<string> HandleAsync(E2EEnumQueryParamsQuery query)
    {
        return Task.FromResult($"status:{query.Status}|priority:{query.Priority}");
    }
}

/// <summary>
/// Enum type used by <see cref="E2EEnumQueryParamsQuery"/> for enum query-string binding tests.
/// </summary>
public enum E2EItemStatus
{
    /// <summary>Unknown status.</summary>
    Unknown = 0,

    /// <summary>Item is active.</summary>
    Active = 1,

    /// <summary>Item is archived.</summary>
    Archived = 2,
}

/// <summary>
/// POST returning AcceptedResult (202) for asynchronous job patterns.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/accepted-job")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EAcceptedJobCommand : ICommand<AcceptedResult<E2EJobAcceptedPayload>>
{
    /// <summary>The name of the job to queue.</summary>
    [Required]
    public string JobName { get; set; } = default!;

    /// <summary>Queues the job and returns an accepted result.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>An accepted result with job details.</returns>
    public static Task<AcceptedResult<E2EJobAcceptedPayload>> HandleAsync(E2EAcceptedJobCommand command)
    {
        return Task.FromResult(new AcceptedResult<E2EJobAcceptedPayload>(new E2EJobAcceptedPayload { JobName = command.JobName, JobId = "job-001" }));
    }
}

/// <summary>Payload returned by <see cref="E2EAcceptedJobCommand"/>.</summary>
public class E2EJobAcceptedPayload
{
    /// <summary>The job name.</summary>
    public string JobName { get; set; } = default!;

    /// <summary>The assigned job ID.</summary>
    public string JobId { get; set; } = default!;
}

/// <summary>
/// POST returning bodyless StatusCodeResult.Accepted (202) for fire-and-forget patterns.
/// Mirrors the legacy <c>ICommand&lt;StatusCodeResult.Accepted&gt;</c> pattern used in Signature365.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/bodyless-accepted")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EBodylessAcceptedCommand : ICommand<StatusCodeResult.Accepted>
{
    /// <summary>Optional reason for the async operation.</summary>
    public string? Reason { get; set; }

    /// <summary>Handles the command and returns a bodyless 202.</summary>
    /// <returns>A bodyless 202 Accepted status code result.</returns>
    public StatusCodeResult.Accepted Handle()
    {
        return StatusCodeResult.Accepted.Instance;
    }
}

/// <summary>
/// POST returning bodyless StatusCodeResult.NoContent (204) for side-effect-only operations.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/bodyless-nocontent")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EBodylessNoContentCommand : ICommand<StatusCodeResult.NoContent>
{
    /// <summary>Optional reason for the operation.</summary>
    public string? Reason { get; set; }

    /// <summary>Handles the command and returns a bodyless 204.</summary>
    /// <returns>A bodyless 204 No Content status code result.</returns>
    public StatusCodeResult.NoContent Handle()
    {
        return StatusCodeResult.NoContent.Instance;
    }
}

/// <summary>
/// POST returning ConflictResult (409) for duplicate-detection patterns.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/conflict-check/{Name}")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EConflictCheckCommand : ICommand<ConflictResult<E2EConflictDetail>>
{
    /// <summary>The name of the resource to check.</summary>
    [Required]
    public string Name { get; set; } = default!;

    /// <summary>Always returns a conflict for testing purposes.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A conflict result with detail.</returns>
    public static Task<ConflictResult<E2EConflictDetail>> HandleAsync(E2EConflictCheckCommand command)
    {
        return Task.FromResult(new ConflictResult<E2EConflictDetail>(new E2EConflictDetail { ExistingName = command.Name }));
    }
}

/// <summary>Detail payload returned by <see cref="E2EConflictCheckCommand"/>.</summary>
public class E2EConflictDetail
{
    /// <summary>The name of the existing conflicting item.</summary>
    public string ExistingName { get; set; } = default!;
}

/// <summary>
/// GET with DateTime and DateTimeOffset query parameters for date binding validation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/datetime-binding")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EDateTimeBindingQuery : IQuery<string>
{
    /// <summary>A DateTime query parameter.</summary>
    public DateTime Since { get; set; }

    /// <summary>A nullable DateTime query parameter.</summary>
    public DateTime? Until { get; set; }

    /// <summary>Echoes back the bound date values.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A formatted string showing the bound dates.</returns>
    public static Task<string> HandleAsync(E2EDateTimeBindingQuery query)
    {
        return Task.FromResult($"since:{query.Since:yyyy-MM-dd}|until:{query.Until?.ToString("yyyy-MM-dd") ?? "null"}");
    }
}

/// <summary>
/// GET with decimal query parameters for precise financial value binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/decimal-binding")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EDecimalBindingQuery : IQuery<string>
{
    /// <summary>A decimal amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>A nullable decimal rate.</summary>
    public decimal? TaxRate { get; set; }

    /// <summary>Echoes back the bound decimal values.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A formatted string showing the bound decimals.</returns>
    public static Task<string> HandleAsync(E2EDecimalBindingQuery query)
    {
        return Task.FromResult($"amount:{query.Amount}|taxRate:{query.TaxRate?.ToString() ?? "null"}");
    }
}

/// <summary>
/// GET with nullable int query parameters for pagination-style binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/nullable-int")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2ENullableIntQuery : IQuery<string>
{
    /// <summary>A required name parameter.</summary>
    [Required]
    public string Name { get; set; } = default!;

    /// <summary>An optional page number.</summary>
    public int? Page { get; set; }

    /// <summary>An optional page size.</summary>
    public int? PageSize { get; set; }

    /// <summary>Echoes back the bound values.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A formatted string showing the bound values.</returns>
    public static Task<string> HandleAsync(E2ENullableIntQuery query)
    {
        return Task.FromResult($"name:{query.Name}|page:{query.Page?.ToString() ?? "null"}|size:{query.PageSize?.ToString() ?? "null"}");
    }
}

/// <summary>
/// GET with bool query parameters for flag-style binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/bool-binding")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EBoolBindingQuery : IQuery<string>
{
    /// <summary>Whether to include archived items.</summary>
    public bool IncludeArchived { get; set; }

    /// <summary>Whether to enable verbose output.</summary>
    public bool Verbose { get; set; }

    /// <summary>Echoes back the bound boolean values.</summary>
    /// <param name="query">The query instance.</param>
    /// <returns>A formatted string showing the bound booleans.</returns>
    public static Task<string> HandleAsync(E2EBoolBindingQuery query)
    {
        return Task.FromResult($"archived:{query.IncludeArchived}|verbose:{query.Verbose}");
    }
}

/// <summary>
/// POST with multiple validation attributes for multi-error reporting.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/multi-validation")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EMultiValidationCommand : ICommand<string>
{
    /// <summary>Required name.</summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = default!;

    /// <summary>Required email.</summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = default!;

    /// <summary>Age must be non-negative.</summary>
    [Range(0, 150)]
    public int Age { get; set; }

    /// <summary>Echoes back the bound values.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A formatted string showing the bound values.</returns>
    public static Task<string> HandleAsync(E2EMultiValidationCommand command)
    {
        return Task.FromResult($"name:{command.Name}|email:{command.Email}|age:{command.Age}");
    }
}

/// <summary>
/// POST returning KeyOnlyResourceEventResult for body-less resource-event patterns.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/key-only-event")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EKeyOnlyEventCommand : ICommand<KeyOnlyResourceEventResult>
{
    /// <summary>The resource ID for the event.</summary>
    [Required]
    public string ResourceId { get; set; } = default!;

    /// <summary>Creates a key-only resource event.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A key-only resource event result.</returns>
    public static Task<KeyOnlyResourceEventResult> HandleAsync(E2EKeyOnlyEventCommand command)
    {
        return Task.FromResult(new KeyOnlyResourceEventResult(Blueprint.Model.ChangeType.Created, "item.created", command.ResourceId));
    }
}

/// <summary>
/// POST returning ResourceEventResult with ChangeType.Updated to verify 200 OK with envelope.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/events-update")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EResourceEventUpdateCommand : ICommand<ResourceEventResult<E2EWidgetResponse>>
{
    /// <summary>Body-bound title for update.</summary>
    public string? Title { get; set; }

    /// <summary>Returns a resource event with Updated change type.</summary>
    /// <param name="command">The command with the body-bound title.</param>
    /// <returns>A ResourceEventResult with ChangeType.Updated.</returns>
    public static Task<ResourceEventResult<E2EWidgetResponse>> HandleAsync(E2EResourceEventUpdateCommand command)
    {
        return Task.FromResult(new ResourceEventResult<E2EWidgetResponse>(
            ChangeType.Updated,
            "evt-update-001",
            "res-update-001",
            new E2EWidgetResponse { Name = command.Title ?? "untitled" }));
    }
}

/// <summary>
/// POST returning ResourceEventResult with ChangeType.Deleted to verify 204 No Content.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/events-delete")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EResourceEventDeleteCommand : ICommand<ResourceEventResult<E2EWidgetResponse>>
{
    /// <summary>The resource ID being deleted.</summary>
    [Required]
    public string ResourceId { get; set; } = default!;

    /// <summary>Returns a resource event with Deleted change type.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A ResourceEventResult with ChangeType.Deleted.</returns>
    public static Task<ResourceEventResult<E2EWidgetResponse>> HandleAsync(E2EResourceEventDeleteCommand command)
    {
        return Task.FromResult(new ResourceEventResult<E2EWidgetResponse>(
            ChangeType.Deleted,
            "evt-delete-001",
            command.ResourceId,
            new E2EWidgetResponse { Name = "deleted" }));
    }
}

/// <summary>
/// POST returning KeyOnlyResourceEventResult with ChangeType.Updated to verify 200 OK.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/key-only-event-update")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EKeyOnlyEventUpdateCommand : ICommand<KeyOnlyResourceEventResult>
{
    /// <summary>The resource ID for the event.</summary>
    [Required]
    public string ResourceId { get; set; } = default!;

    /// <summary>Creates a key-only resource event with Updated change type.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A key-only resource event result with Updated.</returns>
    public static Task<KeyOnlyResourceEventResult> HandleAsync(E2EKeyOnlyEventUpdateCommand command)
    {
        return Task.FromResult(new KeyOnlyResourceEventResult(Blueprint.Model.ChangeType.Updated, "item.updated", command.ResourceId));
    }
}

/// <summary>
/// POST returning KeyOnlyResourceEventResult with ChangeType.Deleted to verify 204 No Content.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/key-only-event-delete")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EKeyOnlyEventDeleteCommand : ICommand<KeyOnlyResourceEventResult>
{
    /// <summary>The resource ID for the event.</summary>
    [Required]
    public string ResourceId { get; set; } = default!;

    /// <summary>Creates a key-only resource event with Deleted change type.</summary>
    /// <param name="command">The command instance.</param>
    /// <returns>A key-only resource event result with Deleted.</returns>
    public static Task<KeyOnlyResourceEventResult> HandleAsync(E2EKeyOnlyEventDeleteCommand command)
    {
        return Task.FromResult(new KeyOnlyResourceEventResult(Blueprint.Model.ChangeType.Deleted, "item.deleted", command.ResourceId));
    }
}

/// <summary>
/// Query that throws <see cref="ApiException"/> with HTTP 503 to verify non-standard status code translation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/service-unavailable")]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EServiceUnavailableQuery : IQuery<string>
{
    /// <summary>Throws ApiException(503) to test custom HTTP status translation.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2EServiceUnavailableQuery query)
    {
        throw new ApiException("Downstream service unavailable", 503);
    }
}

/// <summary>
/// Query with multiple route parameters to verify compound URL binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/parents/{ParentId}/children/{ChildId}")]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EMultiRouteParamQuery : IQuery<string>
{
    /// <summary>The parent resource identifier from the URL.</summary>
    public string ParentId { get; set; } = default!;

    /// <summary>The child resource identifier from the URL.</summary>
    public string ChildId { get; set; } = default!;

    /// <summary>Echoes both route parameters.</summary>
    /// <param name="query">The query with bound route params.</param>
    /// <returns>A string showing both bound values.</returns>
    public static Task<string> HandleAsync(E2EMultiRouteParamQuery query)
    {
        return Task.FromResult($"parent:{query.ParentId}|child:{query.ChildId}");
    }
}

/// <summary>
/// Anonymous query with a <c>TenantId</c> property that is NOT route-bound, used to prove
/// that <see cref="Blueprint.Runtime.Middleware.IPropertyPopulator"/> implementations can
/// auto-populate properties from claims during the Population stage.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/populated-tenant")]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EPopulatedTenantQuery : IQuery<string>
{
    /// <summary>TenantId property populated by the IPropertyPopulator, not from the route.</summary>
    public string? TenantId { get; set; }

    /// <summary>Echoes the populated TenantId to prove population worked.</summary>
    /// <param name="query">The query with potentially populated TenantId.</param>
    /// <returns>A string showing the TenantId value.</returns>
    public static Task<string> HandleAsync(E2EPopulatedTenantQuery query)
    {
        return Task.FromResult($"tenant:{query.TenantId}");
    }
}

/// <summary>
/// Test property populator that sets <c>TenantId</c> from a <c>tenant_id</c> JWT claim,
/// matching the pattern used by Signature365's <c>SetTenantMiddleware</c>.
/// </summary>
internal sealed class TestTenantPropertyPopulator : Blueprint.Runtime.Middleware.IPropertyPopulator
{
    public Task PopulateAsync(Blueprint.Runtime.Exec.ApiOperationContext context, CancellationToken cancellationToken)
    {
        var identity = context.UserIdentity;

        if (identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var tenantClaim = identity.FindFirst("tenant_id");

        if (tenantClaim is null)
        {
            return Task.CompletedTask;
        }

        var operation = context.Operation;
        var tenantIdProp = operation.GetType().GetProperty("TenantId");

        if (tenantIdProp is not null && tenantIdProp.CanWrite)
        {
            var current = tenantIdProp.GetValue(operation) as string;

            if (string.IsNullOrEmpty(current))
            {
                tenantIdProp.SetValue(operation, tenantClaim.Value);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Anonymous POST returning an <see cref="Blueprint.Http.HttpResult" /> — proves the
/// self-executing HTTP result path works end-to-end in the generated pipeline (S365 pattern).
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/e2e/http-result")]
[Blueprint.Model.Attributes.HttpPost]
public partial class E2EHttpResultCommand : ICommand<Blueprint.Http.HttpResult>
{
    public static Task<Blueprint.Http.HttpResult> HandleAsync(E2EHttpResultCommand command)
    {
        return Task.FromResult<Blueprint.Http.HttpResult>(
            new E2ECustomHttpResult(System.Net.HttpStatusCode.Accepted));
    }
}

/// <summary>
/// Concrete <see cref="Blueprint.Http.HttpResult" /> subclass for testing that sets status
/// and a custom header without writing a body.
/// </summary>
internal sealed class E2ECustomHttpResult : Blueprint.Http.HttpResult
{
    public E2ECustomHttpResult(System.Net.HttpStatusCode statusCode)
        : base(statusCode)
    {
        this.Headers["X-Custom-E2E"] = "test-value";
    }
}

/// <summary>
/// Anonymous GET returning a <see cref="Blueprint.Http.ProxyResult" /> — proves that
/// downstream HTTP responses are correctly proxied through the generated pipeline (gateway pattern).
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/e2e/proxy")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EProxyQuery : IQuery<Blueprint.Http.ProxyResult>
{
    /// <summary>Simulates a gateway proxy by constructing a downstream response in-memory.</summary>
    /// <param name="query">The query instance (unused).</param>
    /// <returns>A proxy result wrapping a simulated downstream response.</returns>
    public static Task<Blueprint.Http.ProxyResult> HandleAsync(E2EProxyQuery query)
    {
        var downstream = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"analytics\":\"data\"}", Encoding.UTF8, "application/json"),
        };

        downstream.Headers.Add("X-Downstream-Id", "cubejs-123");
        downstream.Headers.Add("X-Request-Id", "req-456");

        return Task.FromResult(new Blueprint.Http.ProxyResult(downstream));
    }
}
