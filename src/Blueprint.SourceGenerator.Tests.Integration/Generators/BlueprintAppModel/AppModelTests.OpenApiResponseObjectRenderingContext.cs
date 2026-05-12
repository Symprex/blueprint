using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiResponseObjectRenderingContextTests
{
    [Fact]
    public void When_http_response_body_types_are_covered_then_response_object_rendering_context_renders_json_response_objects_from_schema_components()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);

        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);
        var operationContext = responseObjectContext.Operations.Single(static operation => operation.Operation.OperationType == typeof(HttpResponseMetadataQueryOp));

        operationContext.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);

        var okResponse = operationContext.Responses[0];
        okResponse.HasResponseBody.Should().BeTrue();
        okResponse.ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.OkResult<string>");
        okResponse.Content.Select(static content => content.MediaType).Should().Equal("application/json");
        okResponse.Content[0].SchemaComponents.Select(static component => component.BodyTypeName).Should().Equal("string");
        okResponse.Content[0].SchemaComponents[0].Component.Type.Should().Be("string");

        var validationResponse = operationContext.Responses[1];
        validationResponse.HasResponseBody.Should().BeTrue();
        validationResponse.ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.ValidationFailedResult");
        validationResponse.Content.Select(static content => content.MediaType).Should().Equal("application/problem+json");
        validationResponse.Content[0].SchemaComponents.Select(static component => component.BodyTypeName)
            .Should()
            .Equal("ValidationProblemDetails");
        validationResponse.Content[0].SchemaComponents[0].Component.Properties.Select(static property => property.Name)
            .Should()
            .Contain("errors");

        var errorResponse = operationContext.Responses[2];
        errorResponse.HasResponseBody.Should().BeTrue();
        errorResponse.ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.UnhandledExceptionResult");
        errorResponse.Content.Select(static content => content.MediaType).Should().Equal("application/problem+json");
        errorResponse.Content[0].SchemaComponents.Select(static component => component.BodyTypeName)
            .Should()
            .Equal("ProblemDetails");
        errorResponse.Content[0].SchemaComponents[0].Component.Properties.Select(static property => property.Name)
            .Should()
            .Contain(["detail", "status", "title"]);
    }

    [Fact]
    public void When_http_response_body_types_are_not_covered_then_response_object_rendering_context_leaves_response_content_empty()
    {
        var app = new NoSerializerContextBlueprintApp();
        app.Register(HttpResponseMetadataQueryOp.Configure());
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);

        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);
        var operationContext = responseObjectContext.Operations.Single(static operation => operation.Operation.OperationType == typeof(HttpResponseMetadataQueryOp));

        operationContext.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);
        operationContext.Responses.Should().OnlyContain(static response => response.Content.IsEmpty);
    }

    private sealed class NoSerializerContextBlueprintApp : Blueprint.Model.BlueprintApp;
}
