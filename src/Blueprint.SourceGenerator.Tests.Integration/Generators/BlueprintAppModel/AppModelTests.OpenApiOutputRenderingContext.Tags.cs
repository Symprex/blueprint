using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiOutputRenderingContextTagsTests
{
    [Fact]
    public void When_generated_http_operation_has_group_metadata_then_openapi_output_includes_tags()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        var operation = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/openapi/http-response-metadata")
            .GetProperty("get");

        operation.GetProperty("tags")
            .EnumerateArray()
            .Select(static tag => tag.GetString())
            .Should()
            .Equal("OperationDescriptors");
    }

    [Fact]
    public void When_generated_http_operation_declares_apioperations_group_then_openapi_output_uses_explicit_tags()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        var operation = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/openapi/explicit-operation-group")
            .GetProperty("get");

        operation.GetProperty("tags")
            .EnumerateArray()
            .Select(static tag => tag.GetString())
            .Should()
            .Equal("Billing");
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
}
