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

        // x-enum-varnames should mirror enum values for TypeScript code generation
        var varnames = schema.GetProperty("x-enum-varnames").EnumerateArray()
            .Select(static e => e.GetString())
            .ToArray();
        varnames.Should().BeEquivalentTo(["Name", "CreatedDate", "LastModified"]);
    }

    [Fact]
    public async Task When_query_parameter_is_array_type_then_openapi_renders_items_and_style()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // SearchTemplatesQuery has a Tags property of type string[]
        var searchPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");

        var tagsParam = parameters.EnumerateArray()
            .First(static p => p.GetProperty("name").GetString() == "tags");

        // Array query params should have style=form, explode=true
        tagsParam.GetProperty("style").GetString().Should().Be("form");
        tagsParam.GetProperty("explode").GetBoolean().Should().BeTrue();

        // Schema should be array with items sub-schema
        var schema = tagsParam.GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("array");
        schema.GetProperty("items").GetProperty("type").GetString().Should().Be("string");
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
    public async Task When_app_has_mixed_auth_then_security_is_per_operation_not_document_level()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // The sample app has both authenticated and anonymous operations, so security should
        // be per-operation rather than hoisted to the document level.
        doc.RootElement.TryGetProperty("security", out _).Should().BeFalse(
            "mixed-auth apps should use per-operation security, not document-level");

        // Verify per-operation security is present on an authenticated operation
        var createPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        createPath.GetProperty("post").TryGetProperty("security", out _).Should().BeTrue(
            "authenticated operations in mixed-auth apps should have per-operation security blocks");

        // Verify anonymous operations do NOT have security blocks
        var healthPath = doc.RootElement.GetProperty("paths").GetProperty("/system/health");
        healthPath.GetProperty("get").TryGetProperty("security", out _).Should().BeFalse(
            "anonymous operations should not have security blocks");
    }

    [Fact]
    public async Task When_operation_returns_201_then_openapi_renders_location_response_header()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var createPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var responses = createPath.GetProperty("post").GetProperty("responses");
        var created = responses.GetProperty("201");

        created.TryGetProperty("headers", out var headers).Should().BeTrue(
            "201 Created responses should include a headers section");

        headers.TryGetProperty("Location", out var location).Should().BeTrue(
            "201 Created should document the Location header");

        location.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        location.GetProperty("schema").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public async Task When_operation_returns_file_stream_then_openapi_renders_content_disposition_header()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var downloadPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/{Id}/download");
        var responses = downloadPath.GetProperty("get").GetProperty("responses");
        var ok = responses.GetProperty("200");

        ok.TryGetProperty("headers", out var headers).Should().BeTrue(
            "file-stream responses should include a headers section");

        headers.TryGetProperty("Content-Disposition", out var disposition).Should().BeTrue(
            "file-stream responses should document the Content-Disposition header");

        disposition.GetProperty("schema").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public async Task When_operation_returns_redirect_then_openapi_renders_location_header()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var redirectPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/{Id}/docs");
        var responses = redirectPath.GetProperty("get").GetProperty("responses");
        var found = responses.GetProperty("302");

        found.TryGetProperty("headers", out var headers).Should().BeTrue(
            "redirect responses should include a headers section");

        headers.TryGetProperty("Location", out var location).Should().BeTrue(
            "302 Found should document the Location header");

        location.GetProperty("schema").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public async Task When_route_uses_catch_all_then_openapi_renders_allow_reserved()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var folderPath = doc.RootElement.GetProperty("paths").GetProperty("/folders/{**FolderPath}");
        var parameters = folderPath.GetProperty("get").GetProperty("parameters");
        var folderPathParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "FolderPath");

        folderPathParam.GetProperty("allowReserved").GetBoolean().Should().BeTrue(
            "catch-all route parameters should emit allowReserved: true");
    }

    [Fact]
    public async Task When_route_uses_normal_parameter_then_openapi_does_not_render_allow_reserved()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var templatePath = doc.RootElement.GetProperty("paths").GetProperty("/templates/{Id}");
        var parameters = templatePath.GetProperty("get").GetProperty("parameters");
        var idParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "Id");

        idParam.TryGetProperty("allowReserved", out _).Should().BeFalse(
            "regular route parameters should not emit allowReserved");
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

        // Name is nullable (string?) and has [Required] → should still have nullable: true
        // because nullable and required are orthogonal in OpenAPI 3.0.
        // Required means "must be present", nullable means "value can be null".
        var name = bodySchema.GetProperty("name");
        name.TryGetProperty("nullable", out var nameNullable).Should().BeTrue(
            "string? properties should emit nullable: true regardless of [Required] status");
        nameNullable.GetBoolean().Should().BeTrue();
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

    [Fact]
    public async Task When_query_parameter_is_obsolete_then_parameter_emits_deprecated()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // GetPagedTemplatesQuery (GET /templates/paged) has [Obsolete("Use NameFilter instead.")] on CategoryFilter
        var pagedPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/paged");
        var parameters = pagedPath.GetProperty("get").GetProperty("parameters");

        JsonElement? categoryParam = null;
        JsonElement? nameParam = null;

        foreach (var p in parameters.EnumerateArray())
        {
            var name = p.GetProperty("name").GetString();

            if (name == "categoryFilter")
            {
                categoryParam = p;
            }
            else if (name == "nameFilter")
            {
                nameParam = p;
            }
        }

        categoryParam.Should().NotBeNull("CategoryFilter should appear as a query parameter");
        categoryParam!.Value.GetProperty("deprecated").GetBoolean()
            .Should().BeTrue("CategoryFilter is marked [Obsolete]");
        categoryParam!.Value.GetProperty("x-deprecated-reason").GetString()
            .Should().Be("Use NameFilter instead.");

        nameParam.Should().NotBeNull("NameFilter should appear as a query parameter");
        nameParam!.Value.TryGetProperty("deprecated", out _)
            .Should().BeFalse("NameFilter is not obsolete and should not have deprecated");
    }

    [Fact]
    public async Task When_body_property_has_default_value_attribute_then_schema_emits_default()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand (POST /templates) has [DefaultValue(100)] on Priority
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        properties.GetProperty("priority").GetProperty("default").GetInt32()
            .Should().Be(100);

        // Name does NOT have a default — default should be absent
        properties.GetProperty("name").TryGetProperty("default", out _)
            .Should().BeFalse("properties without [DefaultValue] should not emit default");
    }

    [Fact]
    public async Task When_body_property_has_example_attribute_then_schema_emits_example()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand (POST /templates) has [OpenApiExample("Welcome Email")] on Name
        // and [OpenApiExample(42)] on Priority
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        properties.GetProperty("name").GetProperty("example").GetString()
            .Should().Be("Welcome Email");

        properties.GetProperty("priority").GetProperty("example").GetInt32()
            .Should().Be(42);

        // Slug does NOT have an example — example should be absent
        properties.GetProperty("slug").TryGetProperty("example", out _)
            .Should().BeFalse("properties without [OpenApiExample] should not emit example");
    }

    [Fact]
    public async Task When_route_parameter_has_example_attribute_then_parameter_emits_example()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // GetTemplateVersionsQuery (GET /templates/{Id}/versions) has [OpenApiExample("tmpl-001")] on Id
        var versionsPath = doc.RootElement.GetProperty("paths").GetProperty("/templates/{Id}/versions");
        var parameters = versionsPath.GetProperty("get").GetProperty("parameters");

        // Find the Id parameter
        JsonElement? idParam = null;
        foreach (var param in parameters.EnumerateArray())
        {
            if (param.GetProperty("name").GetString() == "Id")
            {
                idParam = param;
                break;
            }
        }

        idParam.Should().NotBeNull("Id parameter should exist");
        idParam!.Value.GetProperty("example").GetString()
            .Should().Be("tmpl-001");
    }

    [Fact]
    public async Task When_body_property_has_writeOnly_attribute_then_schema_emits_writeOnly()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // ChangePasswordCommand (PUT /account/password) has [OpenApiWriteOnly] on all password fields
        var passwordPath = doc.RootElement.GetProperty("paths").GetProperty("/account/password");
        var requestBody = passwordPath.GetProperty("put").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        properties.GetProperty("currentPassword").GetProperty("writeOnly").GetBoolean()
            .Should().BeTrue("password fields marked with [OpenApiWriteOnly] should emit writeOnly: true");
        properties.GetProperty("newPassword").GetProperty("writeOnly").GetBoolean()
            .Should().BeTrue();
        properties.GetProperty("confirmNewPassword").GetProperty("writeOnly").GetBoolean()
            .Should().BeTrue();
    }

    [Fact]
    public async Task When_array_body_property_has_length_constraints_then_schema_emits_minItems_maxItems()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // BulkArchiveTemplatesCommand (POST /templates/bulk-archive) has string[] Ids with [MinLength(1)] [MaxLength(100)]
        var bulkArchivePath = doc.RootElement.GetProperty("paths").GetProperty("/templates/bulk-archive");
        var requestBody = bulkArchivePath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        var idsProperty = properties.GetProperty("ids");
        idsProperty.GetProperty("type").GetString().Should().Be("array");
        idsProperty.GetProperty("minItems").GetInt32().Should().Be(1,
            "[MinLength(1)] on array should map to minItems, not minLength");
        idsProperty.GetProperty("maxItems").GetInt32().Should().Be(100,
            "[MaxLength(100)] on array should map to maxItems, not maxLength");

        // Ensure minLength/maxLength are NOT present (they are string-only constraints)
        idsProperty.TryGetProperty("minLength", out _).Should().BeFalse(
            "array properties should not emit minLength");
        idsProperty.TryGetProperty("maxLength", out _).Should().BeFalse(
            "array properties should not emit maxLength");
    }

    [Fact]
    public async Task When_body_property_is_array_of_primitives_then_items_schema_uses_inline_type()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand (POST /templates) has List<string> Tags
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        var tagsProperty = properties.GetProperty("tags");
        tagsProperty.GetProperty("type").GetString().Should().Be("array");
        tagsProperty.GetProperty("items").GetProperty("type").GetString().Should().Be("string",
            "primitive array element types should render with inline type, not $ref");
    }

    [Fact]
    public async Task When_body_property_is_array_of_complex_type_then_items_schema_renders_as_object()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand (POST /templates) has List<TemplateMergeFieldDto> MergeFields
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        var mergeFieldsProperty = properties.GetProperty("mergeFields");
        mergeFieldsProperty.GetProperty("type").GetString().Should().Be("array");
        mergeFieldsProperty.GetProperty("items").GetProperty("type").GetString().Should().Be("object",
            "complex array element types should render as inline object type");

        // Verify it does NOT have a $ref (no component schema for request-only types)
        mergeFieldsProperty.GetProperty("items").TryGetProperty("$ref", out _)
            .Should().BeFalse("request body complex types should not reference component schemas");
    }

    [Fact]
    public async Task When_property_has_obsolete_with_message_then_x_deprecated_reason_rendered()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand has [Obsolete("Use Tags instead.")] on LegacyCategory
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var requestBody = templatesPath.GetProperty("post").GetProperty("requestBody");
        var properties = requestBody.GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("properties");

        var legacyCategoryProperty = properties.GetProperty("legacyCategory");
        legacyCategoryProperty.GetProperty("deprecated").GetBoolean().Should().BeTrue();
        legacyCategoryProperty.GetProperty("x-deprecated-reason").GetString()
            .Should().Be("Use Tags instead.");
    }

    [Fact]
    public async Task When_operation_has_xml_remarks_then_description_field_rendered()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        // CreateTemplateCommand has <remarks> XML documentation
        var templatesPath = doc.RootElement.GetProperty("paths").GetProperty("/templates");
        var postOperation = templatesPath.GetProperty("post");

        postOperation.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        postOperation.TryGetProperty("description", out var descriptionElement).Should().BeTrue(
            "operations with XML <remarks> should emit an OpenAPI description field");
        descriptionElement.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task When_type_has_display_name_attribute_then_schema_title_uses_display_name()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var doc = JsonDocument.Parse(json);

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        // TemplateApiResource has [DisplayName("Email Template")] — should override default CLR short name
        var templateSchema = schemas.GetProperty("Blueprint_SourceGenerator_Sample_Contract_Api_TemplateApiResource");
        templateSchema.GetProperty("title").GetString().Should().Be("Email Template",
            "[DisplayName(\"Email Template\")] should override the default CLR type name in the schema title");
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

    [Fact]
    public async Task When_openapi_endpoint_invoked_then_returns_etag_and_cache_control_headers()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json", "GET")!;

        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = endpoints.ServiceProvider,
            Response = { Body = body },
        };

        context.SetEndpoint(endpoint);
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/.blueprint/openapi.json";

        await endpoint.RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers.ETag.ToString().Should().NotBeNullOrWhiteSpace();
        context.Response.Headers.CacheControl.ToString().Should().Contain("public");
    }

    [Fact]
    public async Task When_openapi_endpoint_invoked_with_matching_etag_then_returns_304()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");
        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json", "GET")!;

        // First request to get the ETag
        await using var body1 = new MemoryStream();
        var context1 = new DefaultHttpContext
        {
            RequestServices = endpoints.ServiceProvider,
            Response = { Body = body1 },
        };

        context1.SetEndpoint(endpoint);
        context1.Request.Method = HttpMethods.Get;
        context1.Request.Path = "/api/.blueprint/openapi.json";
        await endpoint.RequestDelegate!(context1);

        var etag = context1.Response.Headers.ETag.ToString();
        etag.Should().NotBeNullOrWhiteSpace();

        // Second request with If-None-Match header
        await using var body2 = new MemoryStream();
        var context2 = new DefaultHttpContext
        {
            RequestServices = endpoints.ServiceProvider,
            Response = { Body = body2 },
        };

        context2.SetEndpoint(endpoint);
        context2.Request.Method = HttpMethods.Get;
        context2.Request.Path = "/api/.blueprint/openapi.json";
        context2.Request.Headers.IfNoneMatch = etag;
        await endpoint.RequestDelegate!(context2);

        context2.Response.StatusCode.Should().Be(StatusCodes.Status304NotModified);
        body2.Length.Should().Be(0, "304 responses should not have a body");
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
