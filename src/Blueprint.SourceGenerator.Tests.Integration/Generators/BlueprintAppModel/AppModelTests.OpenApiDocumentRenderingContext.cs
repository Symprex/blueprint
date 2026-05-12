using System.Collections.Immutable;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiDocumentRenderingContextTests
{
    [Fact]
    public void When_generated_http_operation_is_rendered_then_document_rendering_context_assembles_paths_and_schema_components()
    {
        var context = CreateDocumentContext(new IntegrationTestBlueprintApp());

        context.Paths.Select(static path => path.UrlFormat).Should().Contain("/openapi/http-response-metadata");

        var schemaComponentNames = context.SchemaComponents.Select(static component => component.BodyTypeName).ToArray();
        schemaComponentNames.Should().Contain(
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.AlwaysExpandTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.CatchAllFolderApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.ExpandableTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.LinkTestApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MappedLinkApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MultiExpandableTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EConflictDetail",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EJobAcceptedPayload",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EWidgetResponse",
            "Microsoft.AspNetCore.Mvc.ProblemDetails",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "System.Guid",
            "string");

        var requestBodyOperationNames = context.Paths
            .SelectMany(static path => path.Methods)
            .SelectMany(static method => method.Operations)
            .Where(static operation => operation.RequestBody is not null)
            .Select(static operation => operation.OperationName)
            .ToArray();
        schemaComponentNames.Should().Contain(requestBodyOperationNames);

        var path = context.Paths.Single(static item => item.UrlFormat == "/openapi/http-response-metadata");
        path.Methods.Select(static method => method.HttpMethod).Should().Equal("GET");

        var operation = path.Methods[0].Operations.Single();
        operation.OperationName.Should().Be("HttpResponseMetadataQueryOp");
        operation.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);
    }

    [Fact]
    public void When_document_rendering_context_is_created_from_unsorted_inputs_then_paths_and_schema_components_are_ordered_deterministically()
    {
        var context = CreateSyntheticDocumentContext(
            [
                CreatePathItem("/openapi/zeta", "POST", "ZuluOperation", typeof(ZuluOperationShell)),
                CreatePathItem("/openapi/alpha", "GET", "AlphaOperation", typeof(AlphaOperationShell)),
            ],
            [
                CreateSchemaComponent("Zeta.Component"),
                CreateSchemaComponent("Alpha.Component"),
            ]);

        context.Paths.Select(static path => path.UrlFormat).Should().Equal("/openapi/alpha", "/openapi/zeta");
        context.SchemaComponents.Select(static component => component.BodyTypeName).Should().Equal("Alpha.Component", "Zeta.Component");
    }

    [Fact]
    public void When_document_rendering_context_has_no_paths_then_schema_components_are_still_preserved()
    {
        var context = CreateSyntheticDocumentContext(
            [],
            [
                CreateSchemaComponent("Sample.Component"),
            ]);

        context.Paths.Should().BeEmpty();
        context.SchemaComponents.Select(static component => component.BodyTypeName).Should().Equal("Sample.Component");
    }

    [Fact]
    public void When_document_rendering_context_has_no_schema_components_then_paths_are_still_preserved()
    {
        var context = CreateSyntheticDocumentContext(
            [
                CreatePathItem("/openapi/only-path", "GET", "OnlyOperation", typeof(OnlyOperationShell)),
            ],
            []);

        context.Paths.Select(static path => path.UrlFormat).Should().Equal("/openapi/only-path");
        context.Paths[0].Methods.Select(static method => method.HttpMethod).Should().Equal("GET");
        context.SchemaComponents.Should().BeEmpty();
    }

    private static OpenApiDocumentRenderingContext CreateDocumentContext(BlueprintApp app)
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);

        return OpenApiDocumentRenderingContext.Create(pathItemContext);
    }

    private static OpenApiDocumentRenderingContext CreateSyntheticDocumentContext(
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

        return OpenApiDocumentRenderingContext.Create(pathItemContext);
    }

    private static OpenApiPathItemModel CreatePathItem(
        string urlFormat,
        string httpMethod,
        string operationName,
        Type operationType)
    {
        return new OpenApiPathItemModel(
            urlFormat,
            [
                new OpenApiPathItemMethodModel(
                    httpMethod,
                    [
                        new OpenApiOperationModel(
                            operationName,
                            operationType.FullName ?? operationType.Name,
                            [httpMethod],
                            [urlFormat],
                            ImmutableArray<string>.Empty,
                            ImmutableArray<OpenApiResponseObjectModel>.Empty,
                            ImmutableArray<OpenApiParameterModel>.Empty,
                            null),
                    ]),
            ]);
    }

    private static OpenApiSchemaComponentModel CreateSchemaComponent(string bodyTypeName)
    {
        return new OpenApiSchemaComponentModel(
            bodyTypeName,
            "object",
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiSchemaComponentPropertyModel>.Empty);
    }

    private sealed class AlphaOperationShell;

    private sealed class ZuluOperationShell;

    private sealed class OnlyOperationShell;
}
