using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiOperationRenderingContextTagsTests
{
    [Fact]
    public void When_http_operation_publishes_group_metadata_then_operation_rendering_context_exposes_openapi_tags()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);

        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext)
            .Operations.Single(static operation => operation.OperationTypeName == (typeof(HttpResponseMetadataQueryOp).FullName ?? typeof(HttpResponseMetadataQueryOp).Name));

        operationContext.Tags.Should().Equal("OperationDescriptors");
    }

    [Fact]
    public void When_http_operation_declares_apioperations_group_then_operation_rendering_context_uses_explicit_openapi_tag()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);

        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext)
            .Operations.Single(static operation => operation.OperationTypeName == (typeof(ExplicitOperationGroupQueryOp).FullName ?? typeof(ExplicitOperationGroupQueryOp).Name));

        operationContext.Tags.Should().Equal("Billing");
    }
}
