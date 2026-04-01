using System.Collections.Immutable;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiPathItemRenderingContextTests
{
    [Fact]
    public void When_generated_http_operation_is_rendered_then_path_item_rendering_context_preserves_route_method_and_response_metadata()
    {
        var context = CreatePathItemContext(new IntegrationTestBlueprintApp());

        var pathItem = context.PathItems.Single(static item => item.UrlFormat == "/openapi/http-response-metadata");

        pathItem.Methods.Select(static method => method.HttpMethod).Should().Equal("GET");

        var operation = pathItem.Methods[0].Operations.Single();
        operation.OperationName.Should().Be("HttpResponseMetadataQueryOp");
        operation.OperationTypeName.Should().Be(typeof(HttpResponseMetadataQueryOp).FullName ?? typeof(HttpResponseMetadataQueryOp).Name);
        operation.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);
    }

    [Fact]
    public void When_synthetic_operations_share_one_route_then_path_item_rendering_context_groups_them_under_one_path_with_distinct_method_slots()
    {
        var context = CreateSyntheticPathItemContext(
            CreateOperationModel("SharedRouteGetOperation", typeof(SharedRouteGetOperationShell), ["/openapi/shared"], ["GET"]),
            CreateOperationModel("SharedRoutePostOperation", typeof(SharedRoutePostOperationShell), ["/openapi/shared"], ["POST"]));

        context.PathItems.Should().HaveCount(1);

        var pathItem = context.PathItems.Single();
        pathItem.UrlFormat.Should().Be("/openapi/shared");
        pathItem.Methods.Select(static method => method.HttpMethod).Should().Equal("GET", "POST");
        pathItem.Methods[0].Operations.Select(static operation => operation.OperationName).Should().Equal("SharedRouteGetOperation");
        pathItem.Methods[1].Operations.Select(static operation => operation.OperationName).Should().Equal("SharedRoutePostOperation");
    }

    [Fact]
    public void When_synthetic_operations_share_the_same_route_and_method_then_path_item_rendering_context_preserves_them_in_deterministic_order()
    {
        var context = CreateSyntheticPathItemContext(
            CreateOperationModel("ZuluGetOperation", typeof(ZuluGetOperationShell), ["/openapi/conflicted"], ["GET"]),
            CreateOperationModel("AlphaGetOperation", typeof(AlphaGetOperationShell), ["/openapi/conflicted"], ["GET"]));

        context.PathItems.Should().HaveCount(1);

        var pathItem = context.PathItems.Single();
        pathItem.UrlFormat.Should().Be("/openapi/conflicted");
        pathItem.Methods.Select(static method => method.HttpMethod).Should().Equal("GET");
        pathItem.Methods[0].Operations.Select(static operation => operation.OperationName).Should().Equal("AlphaGetOperation", "ZuluGetOperation");
    }

    [Fact]
    public void When_operation_exposes_multiple_urls_and_methods_then_path_item_rendering_context_renders_each_path_with_each_method_slot()
    {
        var context = CreatePathItemContext(new MultiUrlMultiMethodBlueprintApp());

        context.PathItems.Select(static pathItem => pathItem.UrlFormat).Should().Equal("/openapi/alpha", "/openapi/zulu");

        foreach (var pathItem in context.PathItems)
        {
            pathItem.Methods.Select(static method => method.HttpMethod).Should().Equal("GET", "POST");
            pathItem.Methods[0].Operations.Select(static operation => operation.OperationName).Should().Equal("MultiPathOperation");
            pathItem.Methods[1].Operations.Select(static operation => operation.OperationName).Should().Equal("MultiPathOperation");
        }
    }

    [Fact]
    public void When_operation_has_no_urls_then_path_item_rendering_context_does_not_render_a_path_item()
    {
        var context = CreatePathItemContext(new NoUrlBlueprintApp());

        context.PathItems.Should().BeEmpty();
    }

    private static OpenApiPathItemRenderingContext CreatePathItemContext(BlueprintApp app)
    {
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);

        return OpenApiPathItemRenderingContext.Create(operationContext);
    }

    private static OpenApiPathItemRenderingContext CreateSyntheticPathItemContext(params OpenApiOperationModel[] operations)
    {
        var documentContext = new OpenApiDocumentBuilderContext(
            new BlueprintApp(),
            null,
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiDocumentOperationContext>.Empty);
        var schemaProjection = new OpenApiSchemaProjectionContext(documentContext, ImmutableArray<OpenApiSchemaModel>.Empty);
        var schemaComponents = new OpenApiSchemaComponentRenderingContext(schemaProjection, ImmutableArray<OpenApiSchemaComponentModel>.Empty);
        var responseSchemaProjection = new OpenApiResponseSchemaReferenceProjectionContext(
            schemaProjection,
            ImmutableArray<OpenApiResponseSchemaReferenceOperationContext>.Empty);
        var responseObjects = new OpenApiResponseObjectRenderingContext(
            responseSchemaProjection,
            schemaComponents,
            ImmutableArray<OpenApiResponseObjectOperationContext>.Empty);
        var operationRendering = new OpenApiOperationRenderingContext(responseObjects, [.. operations]);

        return OpenApiPathItemRenderingContext.Create(operationRendering);
    }

    private static OpenApiOperationModel CreateOperationModel(
        string operationName,
        Type operationType,
        string[] urlFormats,
        string[] httpMethods)
    {
        return new OpenApiOperationModel(
            operationName,
            operationType.FullName ?? operationType.Name,
            [.. httpMethods],
            [.. urlFormats],
            ImmutableArray<OpenApiResponseObjectModel>.Empty);
    }

    private static ApiOperationDescriptor CreateHttpOperation(
        Type operationType,
        string operationName,
        string[] urlFormats,
        string[] httpMethods)
    {
        var descriptor = new ApiOperationDescriptor(operationType, operationName, "test-fixture");
        descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
        descriptor.SetFeatureData(new OperationHttpMethodMetadataFeatureData([.. httpMethods]));

        foreach (var urlFormat in urlFormats)
        {
            descriptor.AddUrl(new ApiOperationUrl(urlFormat));
        }

        return descriptor;
    }

    private sealed class MultiUrlMultiMethodBlueprintApp : BlueprintApp
    {
        public MultiUrlMultiMethodBlueprintApp()
        {
            this.Register(CreateHttpOperation(
                typeof(MultiPathOperationShell),
                "MultiPathOperation",
                ["/openapi/zulu", "/openapi/alpha"],
                ["POST", "GET"]));
        }
    }

    private sealed class NoUrlBlueprintApp : BlueprintApp
    {
        public NoUrlBlueprintApp()
        {
            this.Register(CreateHttpOperation(typeof(NoUrlOperationShell), "NoUrlOperation", [], ["GET"]));
        }
    }

    private sealed class SharedRouteGetOperationShell;

    private sealed class SharedRoutePostOperationShell;

    private sealed class ZuluGetOperationShell;

    private sealed class AlphaGetOperationShell;

    private sealed class MultiPathOperationShell;

    private sealed class NoUrlOperationShell;
}
