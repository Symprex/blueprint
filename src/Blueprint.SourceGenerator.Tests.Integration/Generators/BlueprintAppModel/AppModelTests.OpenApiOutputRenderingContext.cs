using System.Collections.Immutable;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiOutputRenderingContextTests
{
    [Fact]
    public void When_generated_http_operation_is_rendered_then_output_contains_valid_openapi_document()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());

        context.Json.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.TryGetProperty("info", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("paths", out _).Should().BeTrue();
    }

    [Fact]
    public void When_generated_http_operation_is_rendered_then_paths_section_contains_expected_route_and_method()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("paths").TryGetProperty("/openapi/http-response-metadata", out var pathItem).Should().BeTrue();

        pathItem.TryGetProperty("get", out var operation).Should().BeTrue();
        operation.GetProperty("operationId").GetString().Should().Be("HttpResponseMetadataQueryOp");
    }

    [Fact]
    public void When_generated_http_operation_is_rendered_then_operation_responses_contain_expected_status_codes()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        var responses = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/openapi/http-response-metadata")
            .GetProperty("get")
            .GetProperty("responses");

        responses.TryGetProperty("200", out _).Should().BeTrue();
        responses.TryGetProperty("422", out _).Should().BeTrue();
        responses.TryGetProperty("500", out _).Should().BeTrue();
    }

    [Fact]
    public void When_generated_app_is_rendered_then_components_section_contains_covered_schema_types()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Select(static p => p.Name)
            .Should()
            .Contain(
                "Microsoft.AspNetCore.Mvc.ProblemDetails",
                "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
                "string");
    }

    [Fact]
    public void When_response_has_covered_body_type_then_response_content_uses_schema_ref()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        var response200 = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/openapi/http-response-metadata")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200");

        response200.TryGetProperty("content", out var content).Should().BeTrue();
        content.TryGetProperty("application/json", out var json).Should().BeTrue();
        json.GetProperty("schema").TryGetProperty("$ref", out _).Should().BeTrue();
    }

    [Fact]
    public void When_document_has_no_paths_then_output_still_renders_valid_openapi_document()
    {
        var context = CreateSyntheticOutputContext([], []);

        context.Json.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.GetProperty("paths").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void When_document_has_schema_components_then_components_are_emitted_with_correct_type()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    ["RequiredProp"],
                    [new OpenApiSchemaComponentPropertyModel("RequiredProp", "string", false)]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var schema = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample.ResponseDto");

        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Equal("RequiredProp");
        schema.GetProperty("properties").GetProperty("RequiredProp").GetProperty("type").GetString().Should().Be("string");
    }

    private static OpenApiOutputRenderingContext CreateOutputContext(BlueprintApp app)
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return OpenApiOutputRenderingContext.Create(documentContext);
    }

    private static OpenApiOutputRenderingContext CreateSyntheticOutputContext(
        OpenApiPathItemModel[] pathItems,
        OpenApiSchemaComponentModel[] schemaComponents)
    {
        var documentBuilderContext = new OpenApiDocumentBuilderContext(
            new BlueprintApp(),
            null,
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiDocumentOperationContext>.Empty);
        var schemaProjectionContext = new OpenApiSchemaProjectionContext(documentBuilderContext, ImmutableArray<OpenApiSchemaModel>.Empty);
        var schemaComponentContext = new OpenApiSchemaComponentRenderingContext(schemaProjectionContext, [.. schemaComponents]);
        var responseSchemaProjectionContext = new OpenApiResponseSchemaReferenceProjectionContext(
            schemaProjectionContext,
            ImmutableArray<OpenApiResponseSchemaReferenceOperationContext>.Empty);
        var responseObjectContext = new OpenApiResponseObjectRenderingContext(
            responseSchemaProjectionContext,
            schemaComponentContext,
            ImmutableArray<OpenApiResponseObjectOperationContext>.Empty);
        var operationContext = new OpenApiOperationRenderingContext(
            responseObjectContext,
            ImmutableArray<OpenApiOperationModel>.Empty);
        var pathItemContext = new OpenApiPathItemRenderingContext(operationContext, [.. pathItems]);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return OpenApiOutputRenderingContext.Create(documentContext);
    }
}
