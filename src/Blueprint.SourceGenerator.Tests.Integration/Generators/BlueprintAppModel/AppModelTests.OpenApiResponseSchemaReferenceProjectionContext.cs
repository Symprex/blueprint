using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiResponseSchemaReferenceProjectionContextTests
{
    [Fact]
    public void When_http_response_body_types_are_covered_then_response_schema_reference_projection_context_projects_schema_references_for_each_response()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var operationContext = responseSchemaContext.Operations.Single(static operation => operation.Operation.OperationType == typeof(HttpResponseMetadataQueryOp));

        operationContext.Responses.Select(static response => response.Response.StatusCode).Should().Equal(200, 422, 500);

        var okResponse = operationContext.Responses[0];
        okResponse.Response.ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.OkResult<string>");
        okResponse.Response.BodyTypeNames.Should().Equal("string");
        okResponse.SchemaReferences.Select(static reference => reference.BodyTypeName).Should().Equal("string");
        okResponse.SchemaReferences[0].Schema.BodyTypeName.Should().Be("string");
        okResponse.SchemaReferences[0].Schema.Kind.Should().Be(OpenApiSchemaKind.String);

        var validationResponse = operationContext.Responses[1];
        validationResponse.Response.ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.ValidationFailedResult");
        validationResponse.Response.BodyTypeNames.Should().Equal("Microsoft.AspNetCore.Mvc.ValidationProblemDetails");
        validationResponse.SchemaReferences.Select(static reference => reference.BodyTypeName).Should().Equal("Microsoft.AspNetCore.Mvc.ValidationProblemDetails");
        validationResponse.SchemaReferences[0].Schema.PropertyNames.Should().Contain("errors");

        var errorResponse = operationContext.Responses[2];
        errorResponse.Response.ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.UnhandledExceptionResult");
        errorResponse.Response.BodyTypeNames.Should().Equal("Microsoft.AspNetCore.Mvc.ProblemDetails");
        errorResponse.SchemaReferences.Select(static reference => reference.BodyTypeName).Should().Equal("Microsoft.AspNetCore.Mvc.ProblemDetails");
        errorResponse.SchemaReferences[0].Schema.PropertyNames.Should().Contain(["detail", "status", "title"]);
    }

    [Fact]
    public void When_http_response_body_types_are_not_covered_then_response_schema_reference_projection_context_leaves_schema_references_empty()
    {
        var app = new NoSerializerContextBlueprintApp();
        app.Register(HttpResponseMetadataQueryOp.Configure());
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var operationContext = responseSchemaContext.Operations.Single(static operation => operation.Operation.OperationType == typeof(HttpResponseMetadataQueryOp));

        operationContext.Responses.Select(static response => response.Response.StatusCode).Should().Equal(200, 422, 500);
        operationContext.Responses.Should().OnlyContain(static response => response.SchemaReferences.IsEmpty);
    }

    private sealed class NoSerializerContextBlueprintApp : Blueprint.Model.BlueprintApp;
}
