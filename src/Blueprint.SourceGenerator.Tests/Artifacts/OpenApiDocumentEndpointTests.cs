using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class OpenApiDocumentEndpointTests
{
    [Fact]
    public void When_blueprint_api_is_mapped_then_openapi_document_endpoint_is_registered_at_expected_path()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json");

        endpoint.Should().NotBeNull();
    }

    [Fact]
    public void When_blueprint_api_is_mapped_with_trailing_slash_then_openapi_endpoint_path_is_normalised()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json");

        endpoint.Should().NotBeNull();
    }

    [Fact]
    public async Task When_openapi_endpoint_is_invoked_then_response_is_200_ok_with_json_content_type()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task When_openapi_endpoint_is_invoked_then_body_is_valid_openapi_3x_json()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        response.Body.Should().NotBeNullOrEmpty();

        var doc = JsonDocument.Parse(response.Body);
        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.TryGetProperty("info", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("paths", out _).Should().BeTrue();
    }

    [Fact]
    public void When_blueprint_api_is_mapped_in_production_then_openapi_endpoint_is_still_registered()
    {
        // OpenAPI is a required part of the HTTP host, not a development-only surface.
        var endpoints = CreateEndpoints(Environments.Production);
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json");

        endpoint.Should().NotBeNull();
    }

    [Fact]
    public async Task When_configure_openapi_sets_custom_title_then_info_title_appears_in_rendered_json()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>(
            "api/",
            configureOpenApi: options => options.Title = "My Custom API");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        var doc = JsonDocument.Parse(response.Body);
        doc.RootElement.GetProperty("info").GetProperty("title").GetString().Should().Be("My Custom API");
    }

    [Fact]
    public async Task When_configure_openapi_sets_custom_version_then_info_version_appears_in_rendered_json()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>(
            "api/",
            configureOpenApi: options => options.Version = "2.5.0");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        var doc = JsonDocument.Parse(response.Body);
        doc.RootElement.GetProperty("info").GetProperty("version").GetString().Should().Be("2.5.0");
    }

    [Fact]
    public async Task When_configure_openapi_sets_description_then_info_description_appears_in_rendered_json()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>(
            "api/",
            configureOpenApi: options => options.Description = "A detailed description.");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        var doc = JsonDocument.Parse(response.Body);
        doc.RootElement.GetProperty("info").GetProperty("description").GetString().Should().Be("A detailed description.");
    }

    [Fact]
    public async Task When_configure_openapi_is_not_provided_then_info_uses_default_title_and_version()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        var doc = JsonDocument.Parse(response.Body);
        var info = doc.RootElement.GetProperty("info");
        info.GetProperty("title").GetString().Should().Be("Blueprint API");
        info.GetProperty("version").GetString().Should().Be("1.0.0");
    }

    [Fact]
    public async Task When_schema_processors_are_configured_then_openapi_endpoint_renders_processed_schema_components()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiSchemaComponentBlueprintApp, BlueprintAppExecutor>(
            "api/",
            configureOpenApiSchemas: options =>
            {
                options.Processors.Add(static processorContext =>
                    processorContext.Component = processorContext.Component with
                    {
                        Type = "number",
                    });

                options.Processors.Add(static processorContext =>
                    processorContext.Component = processorContext.Component with
                    {
                        Type = processorContext.Component.Type?.ToUpperInvariant(),
                    });
            });

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        var doc = JsonDocument.Parse(response.Body);
        var schemaKey = typeof(OpenApiSchemaComponentEndpointResponseDto).FullName!.Replace('.', '_');
        doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaKey)
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("NUMBER");
    }

    [Fact]
    public async Task When_openapi_response_represents_same_status_body_bearing_resource_event_union_then_rendered_schema_uses_event_id_discriminator()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiResourceEventUnionBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        var schema = JsonDocument.Parse(response.Body).RootElement
            .GetProperty("paths")
            .GetProperty("/resource-event-union")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("201")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        schema.GetProperty("discriminator").GetProperty("propertyName").GetString().Should().Be("eventId");
        var oneOf = schema.GetProperty("oneOf");
        oneOf.GetArrayLength().Should().Be(2);
        oneOf[0].GetProperty("required").EnumerateArray().Select(static item => item.GetString()).Should().Equal("eventId", "resourceId", "content");
        oneOf[0].GetProperty("properties").GetProperty("eventId").GetProperty("enum")[0].GetString().Should().Be("events.created");
        oneOf[0].GetProperty("properties").GetProperty("content").GetProperty("$ref").GetString().Should().Be("#/components/schemas/Blueprint_SourceGenerator_Tests_Artifacts_OpenApiCreatedEventPayload");
        oneOf[1].GetProperty("properties").GetProperty("eventId").GetProperty("enum")[0].GetString().Should().Be("events.updated");
        oneOf[1].GetProperty("properties").GetProperty("content").GetProperty("$ref").GetString().Should().Be("#/components/schemas/Blueprint_SourceGenerator_Tests_Artifacts_OpenApiUpdatedEventPayload");
    }

    private static async Task<TestEndpointInvocationResult> InvokeEndpointAsync(RouteEndpoint endpoint)
    {
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = body,
            },
        };

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        return new TestEndpointInvocationResult(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static RouteEndpoint? FindEndpoint(TestEndpointRouteBuilder endpoints, string routePattern)
    {
        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(endpoint => string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal));
    }

    private static TestEndpointRouteBuilder CreateEndpoints(string environmentName = "Development")
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(environmentName))
            .AddSingleton<IOptions<BlueprintHttpOptions>>(Options.Create(new BlueprintHttpOptions()))
            .BuildServiceProvider();

        return new TestEndpointRouteBuilder(services);
    }

    private sealed class OpenApiTestBlueprintApp : BlueprintApp
    {
        public OpenApiTestBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "OpenApiDocumentEndpointTests");
            descriptor.AddUrl(new ApiOperationUrl("/test"));
            this.Register(descriptor);
        }
    }

    private sealed class TestOperation;

    private sealed class OpenApiSchemaComponentBlueprintApp : BlueprintApp
    {
        public OpenApiSchemaComponentBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "OpenApiDocumentEndpointTests");
            descriptor.AddUrl(new ApiOperationUrl("/test"));
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.SetFeatureData(new OperationHttpResponseMetadataFeatureData([
                new OperationHttpResponseMetadata(200, true, ["OkResult"], [typeof(OpenApiSchemaComponentEndpointResponseDto).FullName!]),
            ]));
            descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([typeof(OpenApiSchemaComponentEndpointResponseDto).FullName!]));

            this.AppOwnedSerializerContext = new OpenApiSchemaComponentEndpointJsonContext(new JsonSerializerOptions());
            this.AppOwnedSerializerContextType = typeof(OpenApiSchemaComponentEndpointJsonContext);
            this.AppOwnedSerializerContextBodyTypes = [typeof(OpenApiSchemaComponentEndpointResponseDto)];
            this.AppOwnedSerializerContextBodyTypeNames = [typeof(OpenApiSchemaComponentEndpointResponseDto).FullName!];
            this.Register(descriptor);
        }
    }

    private sealed class OpenApiResourceEventUnionBlueprintApp : BlueprintApp
    {
        public OpenApiResourceEventUnionBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(ResourceEventUnionOperation), "ResourceEventUnionOperation", "OpenApiDocumentEndpointTests");
            descriptor.AddUrl(new ApiOperationUrl("/resource-event-union"));
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.SetFeatureData(new OperationHttpMethodMetadataFeatureData(["GET"]));
            descriptor.SetFeatureData(new OperationHttpResponseMetadataFeatureData([
                new OperationHttpResponseMetadata(
                    201,
                    true,
                    [
                        "Blueprint.Runtime.Exec.ResourceEventResult<Blueprint.SourceGenerator.Tests.Artifacts.OpenApiCreatedEventPayload>",
                        "Blueprint.Runtime.Exec.ResourceEventResult<Blueprint.SourceGenerator.Tests.Artifacts.OpenApiUpdatedEventPayload>",
                    ],
                    [
                        typeof(OpenApiCreatedEventPayload).FullName!,
                        typeof(OpenApiUpdatedEventPayload).FullName!,
                    ]),
            ]));
            descriptor.SetFeatureData(new OperationResourceEventMetadataFeatureData([
                new OperationResourceEventMetadata(
                    "Blueprint.Runtime.Exec.ResourceEventResult<Blueprint.SourceGenerator.Tests.Artifacts.OpenApiCreatedEventPayload>",
                    typeof(OpenApiCreatedEventPayload).FullName!,
                    "events.created"),
                new OperationResourceEventMetadata(
                    "Blueprint.Runtime.Exec.ResourceEventResult<Blueprint.SourceGenerator.Tests.Artifacts.OpenApiUpdatedEventPayload>",
                    typeof(OpenApiUpdatedEventPayload).FullName!,
                    "events.updated"),
            ]));
            descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([
                typeof(OpenApiCreatedEventPayload).FullName!,
                typeof(OpenApiUpdatedEventPayload).FullName!,
            ]));

            this.AppOwnedSerializerContext = new OpenApiResourceEventUnionJsonContext(new JsonSerializerOptions());
            this.AppOwnedSerializerContextType = typeof(OpenApiResourceEventUnionJsonContext);
            this.AppOwnedSerializerContextBodyTypes = [typeof(OpenApiCreatedEventPayload), typeof(OpenApiUpdatedEventPayload)];
            this.AppOwnedSerializerContextBodyTypeNames = [typeof(OpenApiCreatedEventPayload).FullName!, typeof(OpenApiUpdatedEventPayload).FullName!];
            this.Register(descriptor);
        }
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

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Blueprint.SourceGenerator.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record TestEndpointInvocationResult(
        int StatusCode,
        string? ContentType,
        string Body);

    private sealed class ResourceEventUnionOperation;
}

public sealed class OpenApiSchemaComponentEndpointResponseDto
{
    public string? Name { get; init; }
}

[JsonSerializable(typeof(OpenApiSchemaComponentEndpointResponseDto))]
public partial class OpenApiSchemaComponentEndpointJsonContext : JsonSerializerContext;

public sealed class OpenApiCreatedEventPayload
{
    public string? DisplayName { get; init; }
}

public sealed class OpenApiUpdatedEventPayload
{
    public int Version { get; init; }
}

[JsonSerializable(typeof(OpenApiCreatedEventPayload))]
[JsonSerializable(typeof(OpenApiUpdatedEventPayload))]
public partial class OpenApiResourceEventUnionJsonContext : JsonSerializerContext;
