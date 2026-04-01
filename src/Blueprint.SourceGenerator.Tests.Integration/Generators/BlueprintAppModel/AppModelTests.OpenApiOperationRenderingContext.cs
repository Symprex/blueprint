using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiOperationRenderingContextTests
{
    [Fact]
    public void When_http_response_objects_are_rendered_then_operation_rendering_context_binds_operation_identity_urls_and_responses()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);
        var registeredDescriptorName = app.FindOperation(typeof(HttpResponseMetadataQueryOp)).Name;

        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext)
            .Operations.Single(static operation => operation.OperationTypeName == (typeof(HttpResponseMetadataQueryOp).FullName ?? typeof(HttpResponseMetadataQueryOp).Name));

        operationContext.OperationName.Should().Be(registeredDescriptorName);
        operationContext.HttpMethods.Should().Equal("GET");
        operationContext.UrlFormats.Should().Equal("/openapi/http-response-metadata");
        operationContext.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);
        operationContext.Responses[0].Content.Select(static content => content.MediaType).Should().Equal("application/json");
    }

    [Fact]
    public void When_http_command_uses_the_conventional_method_then_operation_rendering_context_exposes_post_method_metadata()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);

        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext)
            .Operations.Single(static operation => operation.OperationTypeName == (typeof(HttpMethodConventionCommandOp).FullName ?? typeof(HttpMethodConventionCommandOp).Name));

        operationContext.HttpMethods.Should().Equal("POST");
        operationContext.UrlFormats.Should().Equal("/openapi/http-method-convention-command");
        operationContext.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);
    }

    [Fact]
    public void When_http_operation_declares_multiple_explicit_methods_then_operation_rendering_context_preserves_the_ordered_method_inventory()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);

        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext)
            .Operations.Single(static operation => operation.OperationTypeName == (typeof(HttpMethodMultipleExplicitQueryOp).FullName ?? typeof(HttpMethodMultipleExplicitQueryOp).Name));

        operationContext.HttpMethods.Should().Equal("GET", "POST");
        operationContext.UrlFormats.Should().Equal("/openapi/http-method-multi-explicit-query");
        operationContext.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);
    }

    [Fact]
    public void When_http_operation_publishes_multiple_urls_then_operation_rendering_context_orders_url_formats_deterministically()
    {
        var app = new MultiUrlBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);

        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext)
            .Operations.Single();

        operationContext.OperationName.Should().Be("MultiUrlOperation");
        operationContext.OperationTypeName.Should().Be(typeof(MultiUrlOperationShell).FullName ?? typeof(MultiUrlOperationShell).Name);
        operationContext.UrlFormats.Should().Equal("/openapi/a-primary", "/openapi/z-secondary");
        operationContext.Responses.Should().BeEmpty();
    }

    [Fact]
    public void When_http_operation_has_no_urls_then_operation_rendering_context_preserves_the_operation_with_empty_url_formats()
    {
        var app = new NoUrlBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);

        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext)
            .Operations.Single();

        operationContext.OperationName.Should().Be("NoUrlOperation");
        operationContext.OperationTypeName.Should().Be(typeof(NoUrlOperationShell).FullName ?? typeof(NoUrlOperationShell).Name);
        operationContext.UrlFormats.Should().BeEmpty();
        operationContext.Responses.Should().BeEmpty();
    }

    private sealed class MultiUrlBlueprintApp : BlueprintApp
    {
        public MultiUrlBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(MultiUrlOperationShell), "MultiUrlOperation", "test-fixture");
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.AddUrl(new ApiOperationUrl("/openapi/z-secondary"));
            descriptor.AddUrl(new ApiOperationUrl("/openapi/a-primary"));

            this.Register(descriptor);
        }
    }

    private sealed class NoUrlBlueprintApp : BlueprintApp
    {
        public NoUrlBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(NoUrlOperationShell), "NoUrlOperation", "test-fixture");
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));

            this.Register(descriptor);
        }
    }

    private sealed class MultiUrlOperationShell;

    private sealed class NoUrlOperationShell;
}
