using System.Collections.Immutable;
using System.Text.Json;
using Blueprint.Runtime.Http;
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
    public async Task When_openapi_endpoint_invoked_then_page_parameter_has_exclusive_maximum()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");
        var pageParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "page");

        var schema = pageParam.GetProperty("schema");
        schema.GetProperty("maximum").GetDecimal().Should().Be(1001);
        schema.GetProperty("exclusiveMaximum").GetBoolean().Should().BeTrue();
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

    [Fact]
    public async Task When_body_property_is_nullable_and_not_required_then_openapi_renders_nullable()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand has HtmlSource as string? without [Required]
        var createPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var postOp = createPath.GetProperty("post");
        var bodySchema = postOp.GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("properties");

        // HtmlSource is nullable and not required → should have nullable: true
        var htmlSource = bodySchema.GetProperty("htmlSource");
        htmlSource.TryGetProperty("nullable", out var nullableVal).Should().BeTrue(
            "nullable string? properties without [Required] should emit nullable: true");
        nullableVal.GetBoolean().Should().BeTrue();

        // Name is nullable (string?) but has [Required] → should NOT have nullable
        var name = bodySchema.GetProperty("name");
        name.TryGetProperty("nullable", out _).Should().BeFalse(
            "properties with [Required] should not emit nullable even if CLR type is nullable");
    }

    [Fact]
    public async Task When_servers_configured_then_openapi_renders_servers_array()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync(options =>
        {
            options.Servers.Add(new OpenApiServerEntry { Url = "https://api.example.com/v1", Description = "Production" });
            options.Servers.Add(new OpenApiServerEntry { Url = "https://staging.example.com/v1" });
        });

        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("servers", out var servers).Should().BeTrue(
            "servers section should be present when servers are configured");

        var serverArray = servers.EnumerateArray().ToArray();
        serverArray.Should().HaveCount(2);
        serverArray[0].GetProperty("url").GetString().Should().Be("https://api.example.com/v1");
        serverArray[0].GetProperty("description").GetString().Should().Be("Production");
        serverArray[1].GetProperty("url").GetString().Should().Be("https://staging.example.com/v1");
        serverArray[1].TryGetProperty("description", out _).Should().BeFalse(
            "description should be omitted when not set");
    }

    [Fact]
    public async Task When_no_servers_configured_then_openapi_omits_servers_section()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("servers", out _).Should().BeFalse(
            "servers section should be absent when no servers are configured");
    }

    [Fact]
    public async Task When_operation_returns_array_type_then_openapi_renders_array_schema_with_items_ref()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // GetTemplatesQuery returns IQuery<TemplateApiResource[]> via GET /templates
        var paths = doc.RootElement.GetProperty("paths");
        var templatesGet = paths.GetProperty("/templates").GetProperty("get");
        var responses = templatesGet.GetProperty("responses");

        responses.TryGetProperty("200", out var okResponse).Should().BeTrue(
            "array return types should produce a 200 response");
        var schema = okResponse.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("array");
        schema.TryGetProperty("items", out var items).Should().BeTrue(
            "array schema should have an items property with a $ref to the element type");
        items.GetProperty("$ref").GetString().Should().Contain("TemplateApiResource",
            "items $ref should reference the element type schema component");
    }

    [Fact]
    public async Task When_openapi_info_has_contact_then_contact_object_rendered()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync(info =>
        {
            info.Contact = new OpenApiContactInfo
            {
                Name = "Test Contact",
                Email = "test@example.com",
                Url = "https://example.com/support",
            };
        });

        using var doc = JsonDocument.Parse(json);
        var info = doc.RootElement.GetProperty("info");
        var contact = info.GetProperty("contact");

        contact.GetProperty("name").GetString().Should().Be("Test Contact");
        contact.GetProperty("email").GetString().Should().Be("test@example.com");
        contact.GetProperty("url").GetString().Should().Be("https://example.com/support");
    }

    [Fact]
    public async Task When_openapi_info_has_license_then_license_object_rendered()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync(info =>
        {
            info.License = new OpenApiLicenseInfo
            {
                Name = "MIT",
                Url = "https://opensource.org/licenses/MIT",
            };
        });

        using var doc = JsonDocument.Parse(json);
        var info = doc.RootElement.GetProperty("info");
        var license = info.GetProperty("license");

        license.GetProperty("name").GetString().Should().Be("MIT");
        license.GetProperty("url").GetString().Should().Be("https://opensource.org/licenses/MIT");
    }

    [Fact]
    public async Task When_openapi_info_has_terms_of_service_then_terms_rendered()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync(info =>
        {
            info.TermsOfService = "https://example.com/terms";
        });

        using var doc = JsonDocument.Parse(json);
        var info = doc.RootElement.GetProperty("info");

        info.GetProperty("termsOfService").GetString().Should().Be("https://example.com/terms");
    }

    [Fact]
    public async Task When_openapi_info_has_no_contact_or_license_then_fields_omitted()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);
        var info = doc.RootElement.GetProperty("info");

        info.TryGetProperty("contact", out _).Should().BeFalse("contact should be omitted when not configured");
        info.TryGetProperty("license", out _).Should().BeFalse("license should be omitted when not configured");
        info.TryGetProperty("termsOfService", out _).Should().BeFalse("termsOfService should be omitted when not configured");
    }

    [Fact]
    public async Task When_search_query_has_from_cookie_property_then_openapi_renders_cookie_parameter()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");
        var cookieParam = parameters.EnumerateArray()
            .SingleOrDefault(static p => p.GetProperty("name").GetString() == "pref-locale");

        cookieParam.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "[FromCookie] properties should appear in the OpenAPI parameters list");
        cookieParam.GetProperty("in").GetString().Should().Be("cookie");
        cookieParam.GetProperty("schema").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public async Task When_operation_processor_strips_parameter_then_openapi_omits_it()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync(
            configureOperationProcessors: ops =>
            {
                ops.Processors.Add((operation, descriptor) =>
                {
                    // Strip internal header parameters from the public spec (like S365 strips TenantId)
                    var filtered = operation.Parameters
                        .Where(static p => !string.Equals(p.Name, "X-Correlation-Id", StringComparison.OrdinalIgnoreCase))
                        .ToImmutableArray();

                    return operation with { Parameters = filtered };
                });
            });

        using var doc = JsonDocument.Parse(json);

        // SearchTemplatesQuery has an X-Correlation-Id header parameter.
        // The processor should have stripped it.
        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");
        var correlationParam = parameters.EnumerateArray()
            .Where(static p => string.Equals(p.GetProperty("name").GetString(), "X-Correlation-Id", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        correlationParam.Should().BeEmpty("the operation processor should have stripped the X-Correlation-Id parameter");
    }

    [Fact]
    public async Task When_operation_processor_overrides_summary_then_openapi_reflects_change()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync(
            configureOperationProcessors: ops =>
            {
                ops.Processors.Add((operation, descriptor) =>
                {
                    if (descriptor.Name == "GetTemplatesQuery")
                    {
                        return operation with { Summary = "Custom override summary" };
                    }

                    return operation;
                });
            });

        using var doc = JsonDocument.Parse(json);

        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var getOp = templatesPath.GetProperty("get");
        getOp.GetProperty("summary").GetString().Should().Be("Custom override summary");
    }

    [Fact]
    public async Task When_body_property_has_validation_format_attribute_then_schema_emits_format()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand (POST /templates) has [EmailAddress] on AuthorEmail and [Url] on CallbackUrl
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        properties.GetProperty("authorEmail").GetProperty("format").GetString()
            .Should().Be("email");
        properties.GetProperty("callbackUrl").GetProperty("format").GetString()
            .Should().Be("uri");
    }

    [Fact]
    public async Task When_body_property_is_obsolete_then_schema_emits_deprecated()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand (POST /templates) has [Obsolete] on LegacyCategory
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        // LegacyCategory is marked [Obsolete]
        properties.GetProperty("legacyCategory").GetProperty("deprecated").GetBoolean()
            .Should().BeTrue();

        // AuthorEmail is NOT obsolete — deprecated should be absent
        properties.GetProperty("authorEmail").TryGetProperty("deprecated", out _)
            .Should().BeFalse("non-obsolete properties should not emit deprecated");
    }

    private static async Task<(string Json, int StatusCode)> InvokeOpenApiEndpointAsync(
        Action<OpenApiInfoOptions>? configureOpenApi = null,
        Action<OpenApiOperationProcessorOptions>? configureOperationProcessors = null)
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(
            "api/",
            configureOpenApi: configureOpenApi,
            configureOpenApiOperations: configureOperationProcessors);

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
