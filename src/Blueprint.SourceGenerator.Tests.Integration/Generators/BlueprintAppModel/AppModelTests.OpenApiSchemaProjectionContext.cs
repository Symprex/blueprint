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
        var validationProblemDetailsSchema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == "ValidationProblemDetails");
        var problemDetailsSchema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == "ProblemDetails");

        schemaContext.Schemas.Select(static schema => schema.BodyTypeName).Should().Equal(
            "AlwaysExpandTestEmployee",
            "ArrayEnumParameterInvoiceApiResource",
            "CatchAllFolderApiResource",
            "CollisionHolderApiResource",
            "CollisionItem",
            "CollisionItem2",
            "CollisionItemApiResource",
            "CollisionKindApiResource",
            "CollisionMatch",
            "CollisionMatchApiResource",
            "CollisionPreviewApiResource",
            "CollisionTrailApiResource",
            "DepartmentResource",
            "DictionaryShapeApiResource",
            "DictionaryShapeChild",
            "E2EConflictDetail",
            "E2EJobAcceptedPayload",
            "E2EWidgetResponse",
            "EnumItemsResource",
            "EnvelopeMatch",
            "EnvelopeMatch2",
            "EnvelopeMatch3",
            "ExpandableOfDepartment",
            "ExpandableOfLocation",
            "ExpandableTestEmployee",
            "ExtensionBucketJsonElement",
            "ExtensionBucketNonNullDictionary",
            "ExtensionBucketNonNullValues",
            "ExtensionBucketNullableValues",
            "ExtensionShapeApiResource",
            "Guid",
            "HrefUnlinkedApiResource",
            "HrefWidgetApiResource",
            "HrefWidgetSecretPartialApiResource",
            "LegacyEventWidgetApiResource",
            "LegacyShapeAddress",
            "LegacyShapeBillingAccount",
            "LegacyShapeClosedInfo",
            "LegacyShapeContact",
            "LegacyShapeCountryApiResource",
            "LegacyShapeListApiResourceOfLegacyShapeCountry",
            "LegacyShapeRun",
            "LegacyShapeTenantApiResource",
            "LinkTestApiResource",
            "LinkWireWidgetApiResource",
            "ListApiResourceOfLinkWireWidget",
            "LocationResource",
            "MappedLinkApiResource",
            "MinLengthShapeApiResource",
            "MinLengthShapeToken",
            "MultiExpandableTestEmployee",
            "NodaTimeEchoResponse",
            "PagedApiResourceOfLegacyShapeTenant",
            "ParameterTitlesApiResource",
            "ProblemDetails",
            "RelLessLinkWireWidgetApiResource",
            "RequiredShapeApiResource",
            "RequiredShapeChild",
            "SampleWidgetApiResource",
            "SystemTypeApiResource",
            "TypedIdWidgetApiResource",
            "UnexposedFixtureApiResource",
            "ValidationProblemDetails",
            "XmlDocChild",
            "XmlDocDerived",
            "XmlDocDescribedType",
            "XmlDocGenericOfXmlDocChild",
            "XmlDocShape",
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

    [Fact]
    public void When_body_type_has_description_attribute_then_schema_description_is_projected()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var linkTestSchema = schemaContext.Schemas.Single(
            static schema => schema.BodyTypeName == "LinkTestApiResource");

        linkTestSchema.Description.Should().Be("A test resource used for link population integration tests.");
    }

    [Fact]
    public void When_body_type_has_no_description_attribute_then_schema_description_is_null()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var guidSchema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == "Guid");

        guidSchema.Description.Should().BeNull();
    }

    private sealed class NoSerializerContextBlueprintApp : Blueprint.Model.BlueprintApp;
}
