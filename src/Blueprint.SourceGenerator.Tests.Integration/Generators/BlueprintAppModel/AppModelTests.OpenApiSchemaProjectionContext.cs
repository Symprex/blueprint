using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiSchemaProjectionContextTests
{
    [Fact]
    public void When_http_operation_serializer_requirements_are_covered_then_schema_projection_context_projects_schema_entries_for_the_covered_body_types()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var stringSchema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == "string");
        var validationProblemDetailsSchema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == "Microsoft.AspNetCore.Mvc.ValidationProblemDetails");
        var problemDetailsSchema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == "Microsoft.AspNetCore.Mvc.ProblemDetails");

        schemaContext.Schemas.Select(static schema => schema.BodyTypeName).Should().Equal(
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.AlwaysExpandTestEmployee",
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

        stringSchema.Kind.Should().Be(OpenApiSchemaKind.String);
        stringSchema.IsCoveredBySerializerContext.Should().BeTrue();
        stringSchema.PropertyNames.Should().BeEmpty();

        validationProblemDetailsSchema.Kind.Should().Be(OpenApiSchemaKind.Object);
        validationProblemDetailsSchema.IsCoveredBySerializerContext.Should().BeTrue();
        validationProblemDetailsSchema.PropertyNames.Should().Contain("errors");

        problemDetailsSchema.Kind.Should().Be(OpenApiSchemaKind.Object);
        problemDetailsSchema.IsCoveredBySerializerContext.Should().BeTrue();
        problemDetailsSchema.PropertyNames.Should().Contain(["detail", "status", "title"]);
    }

    [Fact]
    public void When_http_operation_serializer_requirements_are_not_covered_then_schema_projection_context_excludes_missing_body_types()
    {
        var app = new NoSerializerContextBlueprintApp();
        app.Register(HttpResponseMetadataQueryOp.Configure());
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        schemaContext.Schemas.Should().BeEmpty();
    }

    private sealed class NoSerializerContextBlueprintApp : Blueprint.Model.BlueprintApp;
}
