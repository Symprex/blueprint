using System.Text.Json;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the OpenAPI document endpoint renders valid JSON for the reference app and contains
/// the expected paths, methods, and response shapes. This is the "OpenAPI as a reviewable artifact"
/// item for Layer 3.
/// </summary>
public class ReferenceAppOpenApiTests
{
    private const string SnapshotsFolder = "Snapshots/";

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_returns_valid_openapi_json()
    {
        var (json, statusCode) = await InvokeOpenApiEndpointAsync();

        statusCode.Should().Be(StatusCodes.Status200OK);
        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_paths_contain_templates_routes()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var paths = doc.RootElement.GetProperty("paths");

        paths.TryGetProperty("/templates", out _).Should().BeTrue("GET /templates and POST /templates should be in paths");
        paths.TryGetProperty("/templates/{Id}", out _).Should().BeTrue("GET /templates/{Id} and PUT /templates/{Id} should be in paths");
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_templates_path_has_get_and_post()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");

        templatesPath.TryGetProperty("get", out _).Should().BeTrue("GET operation should exist on /templates");
        templatesPath.TryGetProperty("post", out _).Should().BeTrue("POST operation should exist on /templates");
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_template_detail_path_has_get_and_put()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var templateDetailPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/{Id}");

        templateDetailPath.TryGetProperty("get", out _).Should().BeTrue("GET operation should exist on /templates/{Id}");
        templateDetailPath.TryGetProperty("put", out _).Should().BeTrue("PUT operation should exist on /templates/{Id}");
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_info_section_is_present()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var info = doc.RootElement.GetProperty("info");
        info.TryGetProperty("title", out _).Should().BeTrue();
        info.TryGetProperty("version", out _).Should().BeTrue();
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_full_document_matches_reviewed_snapshot_Async()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();

        // Parse and re-serialize with indentation for stable, readable snapshot baselines.
        using var doc = JsonDocument.Parse(json);
        var indented = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });

        await Verify(indented)
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_search_query_parameter_has_required_and_minlength()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");
        var qParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "q");

        qParam.GetProperty("required").GetBoolean().Should().BeTrue("Q has [Required]");
        qParam.GetProperty("schema").GetProperty("minLength").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_page_parameter_has_exclusive_minimum()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");
        var pageParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "page");

        var schema = pageParam.GetProperty("schema");
        schema.GetProperty("minimum").GetDecimal().Should().Be(0);
        schema.GetProperty("exclusiveMinimum").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_page_size_parameter_has_range_constraints()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");
        var pageSizeParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "pageSize");

        var schema = pageSizeParam.GetProperty("schema");
        schema.GetProperty("minimum").GetDecimal().Should().Be(1);
        schema.GetProperty("maximum").GetDecimal().Should().Be(100);
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_create_request_body_has_required_array_and_max_length()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var schema = requestBody.GetProperty("content").GetProperty("application/json").GetProperty("schema");

        schema.GetProperty("required").EnumerateArray()
            .Select(static e => e.GetString())
            .Should().Contain("name");

        var nameProperty = schema.GetProperty("properties").GetProperty("name");
        nameProperty.GetProperty("maxLength").GetInt32().Should().Be(200);

        var htmlSourceProperty = schema.GetProperty("properties").GetProperty("htmlSource");
        htmlSourceProperty.GetProperty("maxLength").GetInt32().Should().Be(50000);
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_operation_summary_is_rendered_from_description_attribute()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var getOperation = searchPath.GetProperty("get");

        getOperation.GetProperty("summary").GetString()
            .Should().Be("Search templates by name with pagination support.");
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_parameter_descriptions_are_rendered()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");
        var qParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "q");

        qParam.GetProperty("description").GetString()
            .Should().Be("The search term to filter templates by name.");
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_request_body_property_descriptions_are_rendered()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var schema = requestBody.GetProperty("content").GetProperty("application/json").GetProperty("schema");

        var nameProperty = schema.GetProperty("properties").GetProperty("name");
        nameProperty.GetProperty("description").GetString()
            .Should().Be("The display name for the template.");

        var htmlSourceProperty = schema.GetProperty("properties").GetProperty("htmlSource");
        htmlSourceProperty.GetProperty("description").GetString()
            .Should().Be("The HTML source content of the template.");
    }

    [Fact]
    public async Task When_operation_marked_obsolete_then_openapi_shows_deprecated()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // GetTemplateVersionsQuery is marked [Obsolete] and should render deprecated: true
        var versionsPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/{Id}/versions");
        var getOperation = versionsPath.GetProperty("get");
        getOperation.GetProperty("deprecated").GetBoolean().Should().BeTrue();

        // GetTemplateQuery is NOT obsolete — deprecated should be absent
        var templatePath = doc.RootElement.GetProperty("paths").GetProperty("/templates/{Id}");
        var getTemplateOp = templatePath.GetProperty("get");
        getTemplateOp.TryGetProperty("deprecated", out _).Should().BeFalse(
            "non-obsolete operations should not emit the deprecated field");
    }

    [Fact]
    public async Task When_property_is_enum_then_openapi_renders_enum_values()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // SearchTemplatesQuery has a SortBy property of type TemplateSortOrder enum
        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");

        var sortByParam = parameters.EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "sortBy");

        var schema = sortByParam.GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("string");

        var enumArray = schema.GetProperty("enum").EnumerateArray()
            .Select(static e => e.GetString())
            .ToArray();
        enumArray.Should().BeEquivalentTo(["Name", "CreatedDate", "LastModified"]);
    }

    [Fact]
    public async Task When_operation_requires_authentication_then_openapi_renders_security_requirement()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand requires authentication (MustBeAuthenticated)
        var createPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var postOp = createPath.GetProperty("post");

        postOp.TryGetProperty("security", out var security).Should().BeTrue(
            "authenticated operations should emit a security requirement");
        var scheme = security.EnumerateArray().First();
        scheme.TryGetProperty("bearerAuth", out _).Should().BeTrue();
    }

    [Fact]
    public async Task When_operations_require_authentication_then_components_include_security_scheme()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var components = doc.RootElement.GetProperty("components");
        components.TryGetProperty("securitySchemes", out var schemes).Should().BeTrue(
            "components should include securitySchemes when authenticated operations exist");

        var bearerAuth = schemes.GetProperty("bearerAuth");
        bearerAuth.GetProperty("type").GetString().Should().Be("http");
        bearerAuth.GetProperty("scheme").GetString().Should().Be("bearer");
        bearerAuth.GetProperty("bearerFormat").GetString().Should().Be("JWT");
    }

    private static async Task<(string Json, int StatusCode)> InvokeOpenApiEndpointAsync()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json", "GET");
        endpoint.Should().NotBeNull("the OpenAPI endpoint should be registered");

        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = endpoints.ServiceProvider,
            Response = { Body = body },
        };

        context.SetEndpoint(endpoint);
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/.blueprint/openapi.json";

        await endpoint!.RequestDelegate!(context);
        body.Position = 0;

        var json = await new StreamReader(body).ReadToEndAsync();
        return (json, context.Response.StatusCode);
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
            .AddSingleton<SampleBlueprintAppExecutor>();

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
}
