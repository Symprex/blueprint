using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiDocumentBuilderContextTests
{
    [Fact]
    public void When_http_operation_serializer_requirements_are_declared_by_the_app_then_openapi_document_builder_context_marks_the_operation_as_covered()
    {
        var app = new IntegrationTestBlueprintApp();

        var context = OpenApiDocumentBuilderContext.Create(app);
        var operationContext = context.Operations.Single(static operation => operation.Operation.OperationType == typeof(HttpResponseMetadataQueryOp));

        context.DeclaredSerializerContextType.Should().Be(typeof(IntegrationTestAppJsonContext));
        context.DeclaredSerializerContextBodyTypeNames.Should().Equal(
            "string",
            "System.Guid",
            "Microsoft.AspNetCore.Mvc.ProblemDetails",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.LinkTestApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MappedLinkApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.ResourceWithoutSelfLink",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.CatchAllFolderApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EWidgetResponse",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EJobAcceptedPayload",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EConflictDetail",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.ExpandableTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.AlwaysExpandTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MultiExpandableTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Http.NodaTimeEchoResponse",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.SelfQueryTestApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel.SampleWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel.UnexposedFixtureApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeTenantApiResource",
            "Blueprint.Model.PagedApiResource<Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeTenantApiResource>",
            "Blueprint.Http.ResourceEvent<Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeTenantApiResource>",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeListApiResource<Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeCountryApiResource>",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeGatewayRunApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionHolderApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionTrailApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionPreviewApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionOne.CollisionItem",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionTwo.CollisionItem",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyEventWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.TypedIdWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.RequiredShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.ConverterWrittenBodyId",
            "Blueprint.SourceGenerator.Tests.Integration.Http.HrefWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.HrefWidgetSecretPartialApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.HrefUnlinkedApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.DictionaryShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.ExtensionShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.ParameterTitlesApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.XmlDocShape",
            "Blueprint.SourceGenerator.Tests.Integration.Http.NativeEventGadget",
            "Blueprint.SourceGenerator.Tests.Integration.Http.MinLengthShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LinkWireWidgetApiResource",
            "Blueprint.Model.ListApiResource<Blueprint.SourceGenerator.Tests.Integration.Http.LinkWireWidgetApiResource>");
        context.Operations.Select(static operation => operation.Operation.OperationType)
            .Should()
            .NotContain(typeof(InternalOnlyOperationToScan))
            .And.NotContain(typeof(BackgroundTaskOperationToScan));

        operationContext.RequiredSerializerContextBodyTypeNames.Should().Equal(
            "string",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "Microsoft.AspNetCore.Mvc.ProblemDetails");
        operationContext.MissingSerializerContextBodyTypeNames.Should().BeEmpty();
        operationContext.IsSerializerContextCoverageSatisfied.Should().BeTrue();
        operationContext.Responses.Select(static response => response.StatusCode).Should().Equal(200, 422, 500);
    }

    [Fact]
    public void When_http_operation_serializer_requirements_are_not_declared_by_the_app_then_openapi_document_builder_context_reports_the_missing_body_types()
    {
        var app = new NoSerializerContextBlueprintApp();
        app.Register(HttpResponseMetadataQueryOp.Configure());

        var context = OpenApiDocumentBuilderContext.Create(app);
        var operationContext = context.Operations.Single(static operation => operation.Operation.OperationType == typeof(HttpResponseMetadataQueryOp));

        context.DeclaredSerializerContextType.Should().BeNull();
        context.DeclaredSerializerContextBodyTypeNames.Should().BeEmpty();
        operationContext.RequiredSerializerContextBodyTypeNames.Should().Equal(
            "string",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "Microsoft.AspNetCore.Mvc.ProblemDetails");
        operationContext.MissingSerializerContextBodyTypeNames.Should().Equal(
            "string",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "Microsoft.AspNetCore.Mvc.ProblemDetails");
        operationContext.IsSerializerContextCoverageSatisfied.Should().BeFalse();
    }

    private sealed class NoSerializerContextBlueprintApp : Blueprint.Model.BlueprintApp;
}
