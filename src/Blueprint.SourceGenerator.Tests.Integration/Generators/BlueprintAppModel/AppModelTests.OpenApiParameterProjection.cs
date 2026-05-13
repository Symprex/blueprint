using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

/// <summary>
/// Proves that the OpenAPI operation rendering context projects parameters and request body from
/// the compile-time property ownership feature data.
/// </summary>
public class AppModelOpenApiParameterProjectionTests
{
    [Fact]
    public void When_post_command_with_route_and_body_then_operation_has_path_parameter_and_request_body()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<UpdateUserPopulationCommand>(operationContext);

        operation.Parameters.Should().ContainSingle(static p => p.Name == "Id" && p.In == "path" && p.Required);
        operation.RequestBody.Should().NotBeNull();
        operation.RequestBody!.Required.Should().BeTrue();
        operation.RequestBody.Properties.Should().Contain(static p => p.Name == "name");
        operation.RequestBody.Properties.Should().Contain(static p => p.Name == "email");
    }

    [Fact]
    public void When_get_query_with_route_and_query_params_then_operation_has_path_and_query_parameters()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<GetUserPopulationQuery>(operationContext);

        operation.Parameters.Should().Contain(static p => p.Name == "Id" && p.In == "path" && p.Required);
        operation.Parameters.Should().Contain(static p => p.Name == "name" && p.In == "query" && !p.Required);
        operation.RequestBody.Should().BeNull("GET operations should not have a request body");
    }

    [Fact]
    public void When_query_only_get_then_operation_has_query_parameters_with_correct_types()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<SearchPopulationQuery>(operationContext);

        operation.Parameters.Should().Contain(static p => p.Name == "term" && p.In == "query" && p.SchemaType == "string");
        operation.Parameters.Should().Contain(static p => p.Name == "pageSize" && p.In == "query" && p.SchemaType == "integer" && p.SchemaFormat == "int32");
        operation.Parameters.Should().Contain(static p => p.Name == "includeArchived" && p.In == "query" && p.SchemaType == "boolean");
        operation.RequestBody.Should().BeNull();
    }

    [Fact]
    public void When_delete_with_route_and_query_then_operation_has_both_parameter_kinds()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<DeleteItemPopulationCommand>(operationContext);

        operation.Parameters.Should().Contain(static p => p.Name == "Id" && p.In == "path");
        operation.Parameters.Should().Contain(static p => p.Name == "force" && p.In == "query" && p.SchemaType == "boolean");
        operation.RequestBody.Should().BeNull("DELETE operations should not have a request body");
    }

    [Fact]
    public void When_explicit_header_binding_then_operation_has_header_parameter()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<HeaderBoundPopulationQuery>(operationContext);

        operation.Parameters.Should().ContainSingle(static p => p.Name == "X-Tenant-Id" && p.In == "header" && !p.Required && p.SchemaType == "string");
    }

    [Fact]
    public void When_explicit_cookie_binding_then_operation_has_cookie_parameter()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<CookieBoundPopulationQuery>(operationContext);

        operation.Parameters.Should().ContainSingle(static p => p.Name == "session" && p.In == "cookie" && !p.Required && p.SchemaType == "string");
    }

    [Fact]
    public void When_mixed_binding_sources_then_operation_has_all_parameter_types_and_request_body()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<MixedBindingPopulationCommand>(operationContext);

        operation.Parameters.Should().Contain(static p => p.Name == "OrgId" && p.In == "path" && p.Required);
        operation.Parameters.Should().Contain(static p => p.Name == "X-Tenant" && p.In == "header" && !p.Required);
        operation.Parameters.Should().Contain(static p => p.Name == "auth_token" && p.In == "cookie" && !p.Required);
        operation.Parameters.Should().Contain(static p => p.Name == "filter" && p.In == "query" && !p.Required);

        operation.RequestBody.Should().NotBeNull();
        operation.RequestBody!.Properties.Should().ContainSingle(static p => p.Name == "name");
    }

    [Fact]
    public void When_int_route_parameter_then_schema_type_is_integer()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<GetUserByIntIdQuery>(operationContext);

        operation.Parameters.Should().ContainSingle(static p => p.Name == "Id" && p.In == "path" && p.SchemaType == "integer" && p.SchemaFormat == "int32");
    }

    [Fact]
    public void When_parameters_are_rendered_to_openapi_json_then_output_contains_parameters_array()
    {
        var app = new IntegrationTestBlueprintApp();
        var output = BuildOutputContext(app);
        var doc = JsonDocument.Parse(output.Json);
        var paths = doc.RootElement.GetProperty("paths");

        // SearchPopulationQuery has GET /population-test/search with query params
        var searchPath = paths.GetProperty("/population-test/search");
        var getMethod = searchPath.GetProperty("get");
        var parameters = getMethod.GetProperty("parameters");

        parameters.GetArrayLength().Should().BeGreaterThan(0);

        var paramNames = parameters.EnumerateArray()
            .Select(static p => p.GetProperty("name").GetString())
            .ToList();

        paramNames.Should().Contain("term");
        paramNames.Should().Contain("pageSize");
        paramNames.Should().Contain("includeArchived");
    }

    [Fact]
    public void When_request_body_is_rendered_to_openapi_json_then_output_contains_request_body_object()
    {
        var app = new IntegrationTestBlueprintApp();
        var output = BuildOutputContext(app);
        var doc = JsonDocument.Parse(output.Json);
        var paths = doc.RootElement.GetProperty("paths");

        // UpdateUserPopulationCommand has POST /population-test/users-update/{Id} with body
        var updatePath = paths.GetProperty("/population-test/users-update/{Id}");
        var postMethod = updatePath.GetProperty("post");

        postMethod.TryGetProperty("requestBody", out var requestBody).Should().BeTrue();
        requestBody.GetProperty("required").GetBoolean().Should().BeTrue();

        var schema = ResolveRequestBodySchema(requestBody, doc.RootElement);

        schema.GetProperty("type").GetString().Should().Be("object");

        var properties = schema.GetProperty("properties");
        properties.TryGetProperty("name", out _).Should().BeTrue();
        properties.TryGetProperty("email", out _).Should().BeTrue();
    }

    [Fact]
    public void When_parameter_has_format_then_openapi_json_includes_schema_format()
    {
        var app = new IntegrationTestBlueprintApp();
        var output = BuildOutputContext(app);
        var doc = JsonDocument.Parse(output.Json);
        var paths = doc.RootElement.GetProperty("paths");

        // SearchPopulationQuery → GET /population-test/search has int PageSize
        var searchPath = paths.GetProperty("/population-test/search");
        var parameters = searchPath.GetProperty("get").GetProperty("parameters");

        var pageSizeParam = parameters.EnumerateArray()
            .Single(static p => p.GetProperty("name").GetString() == "pageSize");

        pageSizeParam.GetProperty("schema").GetProperty("type").GetString().Should().Be("integer");
        pageSizeParam.GetProperty("schema").GetProperty("format").GetString().Should().Be("int32");
    }

    [Fact]
    public void When_no_ownership_data_then_operation_has_empty_parameters_and_no_request_body()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        // HttpResponseMetadataQueryOp has no route/query/body properties in the ownership plan
        var operation = FindOperation<HttpResponseMetadataQueryOp>(operationContext);

        operation.Parameters.Should().BeEmpty();
        operation.RequestBody.Should().BeNull();
    }

    [Fact]
    public void When_post_command_has_FromAuthContext_property_then_openapi_body_excludes_it()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<CreateAuthContextItemCommand>(operationContext);

        // TenantId has [FromAuthContext] — must NOT appear in body
        operation.RequestBody.Should().NotBeNull();
        operation.RequestBody!.Properties.Should().NotContain(static p => p.Name == "tenantId");

        // Name is a regular body property — must appear
        operation.RequestBody.Properties.Should().ContainSingle(static p => p.Name == "name");
    }

    [Fact]
    public void When_get_query_has_FromAuthContext_property_then_openapi_query_params_exclude_it()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<ListAuthContextItemsQuery>(operationContext);

        // TenantId has [FromAuthContext] — must NOT appear as query parameter
        operation.Parameters.Should().NotContain(static p => p.Name == "TenantId");
        operation.Parameters.Should().NotContain(static p => p.Name == "tenantId");

        // Page is a regular query property — must appear
        operation.Parameters.Should().ContainSingle(static p => p.Name == "page" && p.In == "query");
    }

    [Fact]
    public void When_FromAuthContext_is_also_route_placeholder_then_openapi_includes_it_as_path_param()
    {
        var app = new IntegrationTestBlueprintApp();
        var operationContext = BuildOperationContext(app);

        var operation = FindOperation<GetAuthContextRouteItemQuery>(operationContext);

        // TenantId is both [FromAuthContext] and a route placeholder — route wins
        operation.Parameters.Should().Contain(static p => p.Name == "TenantId" && p.In == "path");

        // Id is a regular route param
        operation.Parameters.Should().Contain(static p => p.Name == "Id" && p.In == "path");
    }

    [Fact]
    public void When_post_command_has_FromAuthContext_then_openapi_json_body_schema_excludes_it()
    {
        var app = new IntegrationTestBlueprintApp();
        var output = BuildOutputContext(app);
        var doc = JsonDocument.Parse(output.Json);
        var paths = doc.RootElement.GetProperty("paths");

        var authContextPath = paths.GetProperty("/population-test/auth-context-items");
        var postMethod = authContextPath.GetProperty("post");

        postMethod.TryGetProperty("requestBody", out var requestBody).Should().BeTrue();
        var schema = ResolveRequestBodySchema(requestBody, doc.RootElement);
        var properties = schema.GetProperty("properties");

        // TenantId must NOT appear in the JSON body schema
        properties.TryGetProperty("tenantId", out _).Should().BeFalse(
            "[FromAuthContext] properties should not appear in OpenAPI request body schema");

        // Name must appear
        properties.TryGetProperty("name", out _).Should().BeTrue();
    }

    private static JsonElement ResolveRequestBodySchema(JsonElement requestBody, JsonElement root)
    {
        var schemaElement = requestBody.GetProperty("content").GetProperty("application/json").GetProperty("schema");

        if (schemaElement.TryGetProperty("$ref", out var refElement))
        {
            var refPath = refElement.GetString()!;
            var schemaName = refPath.Split('/').Last();
            return root.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);
        }

        return schemaElement;
    }

    private static OpenApiOperationRenderingContext BuildOperationContext(BlueprintApp app)
    {
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);

        return OpenApiOperationRenderingContext.Create(responseObjectContext);
    }

    private static OpenApiOutputRenderingContext BuildOutputContext(BlueprintApp app)
    {
        var operationContext = BuildOperationContext(app);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return OpenApiOutputRenderingContext.Create(documentContext);
    }

    private static OpenApiOperationModel FindOperation<T>(OpenApiOperationRenderingContext context)
    {
        var typeName = typeof(T).FullName ?? typeof(T).Name;

        return context.Operations.Single(operation => operation.OperationTypeName == typeName);
    }
}
