using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiSchemaComponentRenderingContextTests
{
    [Fact]
    public void When_http_operation_serializer_requirements_are_covered_then_schema_component_rendering_context_renders_components_for_the_covered_body_types()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var componentContext = OpenApiSchemaComponentRenderingContext.Create(schemaContext);

        componentContext.Components.Select(static component => component.BodyTypeName).Should().Equal(
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
            "TypedIdWidgetApiResource",
            "UnexposedFixtureApiResource",
            "ValidationProblemDetails",
            "XmlDocChild",
            "XmlDocDerived",
            "XmlDocDescribedType",
            "XmlDocGenericOfXmlDocChild",
            "XmlDocShape",
            "string");

        var stringComponent = componentContext.Components.Single(static component => component.BodyTypeName == "string");
        stringComponent.Type.Should().Be("string");
        stringComponent.RequiredPropertyNames.Should().BeEmpty();
        stringComponent.Properties.Should().BeEmpty();

        var problemDetailsComponent = componentContext.Components.Single(static component => component.BodyTypeName == "ProblemDetails");
        problemDetailsComponent.Type.Should().Be("object");
        problemDetailsComponent.RequiredPropertyNames.Should().Equal("status");
        problemDetailsComponent.Properties.Select(static property => property.Name).Should().Contain(["detail", "status", "title"]);

        var validationProblemDetailsComponent = componentContext.Components.Single(static component => component.BodyTypeName == "ValidationProblemDetails");
        validationProblemDetailsComponent.Type.Should().Be("object");
        validationProblemDetailsComponent.Properties.Select(static property => property.Name).Should().Contain("errors");
    }

    [Fact]
    public void When_http_operation_serializer_requirements_are_not_covered_then_schema_component_rendering_context_renders_no_components()
    {
        var app = new NoSerializerContextBlueprintApp();
        app.Register(HttpResponseMetadataQueryOp.Configure());
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var componentContext = OpenApiSchemaComponentRenderingContext.Create(schemaContext);

        componentContext.Components.Should().BeEmpty();
    }

    [Fact]
    public void When_schema_projection_includes_property_metadata_then_schema_component_rendering_context_renders_required_properties_and_extension_data_markers()
    {
        var app = new PropertyMetadataSchemaComponentBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var componentContext = OpenApiSchemaComponentRenderingContext.Create(schemaContext);
        var component = componentContext.Components.Single(static component => component.BodyTypeName == nameof(SchemaPropertyMetadataResponse));

        component.Type.Should().Be("object");
        component.RequiredPropertyNames.Should().Equal("Id");
        component.HasExtensionData.Should().BeTrue("because SchemaPropertyMetadataResponse has a [JsonExtensionData] property");
        component.Properties.Select(static property => property.Name).Should().Equal("Child", "Count", "Enabled", "Extensions", "Id", "Metadata", "Tags");

        var childProperty = component.Properties.Single(static property => property.Name == "Child");
        childProperty.Type.Should().Be("object");
        childProperty.IsExtensionData.Should().BeFalse();

        var countProperty = component.Properties.Single(static property => property.Name == "Count");
        countProperty.Type.Should().Be("integer");
        countProperty.IsExtensionData.Should().BeFalse();

        var enabledProperty = component.Properties.Single(static property => property.Name == "Enabled");
        enabledProperty.Type.Should().Be("boolean");
        enabledProperty.IsExtensionData.Should().BeFalse();

        // The [JsonExtensionData] bucket is documented as a property of its own, as legacy documented it.
        var extensionsProperty = component.Properties.Single(static property => property.Name == "Extensions");
        extensionsProperty.Type.Should().Be("object");
        extensionsProperty.IsExtensionData.Should().BeFalse();

        var idProperty = component.Properties.Single(static property => property.Name == "Id");
        idProperty.Type.Should().Be("string");
        idProperty.IsExtensionData.Should().BeFalse();

        var metadataProperty = component.Properties.Single(static property => property.Name == "Metadata");
        metadataProperty.Type.Should().Be("object");
        metadataProperty.IsExtensionData.Should().BeFalse();

        var tagsProperty = component.Properties.Single(static property => property.Name == "Tags");
        tagsProperty.Type.Should().Be("array");
        tagsProperty.IsExtensionData.Should().BeFalse();
    }

    [Fact]
    public void When_schema_projection_includes_validation_constraints_then_schema_component_rendering_context_renders_openapi_constraint_fields()
    {
        var app = new ValidationConstraintBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var componentContext = OpenApiSchemaComponentRenderingContext.Create(schemaContext);
        var component = componentContext.Components.Single(static component => component.BodyTypeName == nameof(SchemaValidationConstraintsResponse));

        var codeProperty = component.Properties.Single(static property => property.Name == "Code");
        codeProperty.MinLength.Should().Be(4);
        codeProperty.MaxLength.Should().Be(12);
        codeProperty.Pattern.Should().Be("^[A-Z0-9]+$");

        var positiveCountProperty = component.Properties.Single(static property => property.Name == "PositiveCount");
        positiveCountProperty.Minimum.Should().Be(0m);
        positiveCountProperty.ExclusiveMinimum.Should().BeTrue();

        var quantityProperty = component.Properties.Single(static property => property.Name == "Quantity");
        quantityProperty.Minimum.Should().Be(1m);
        quantityProperty.Maximum.Should().Be(10m);
        quantityProperty.ExclusiveMinimum.Should().BeFalse();
        quantityProperty.ExclusiveMaximum.Should().BeFalse();
    }

    [Fact]
    public void When_body_type_has_description_attribute_then_schema_component_includes_description()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var componentContext = OpenApiSchemaComponentRenderingContext.Create(schemaContext);
        var linkTestComponent = componentContext.Components.Single(
            static component => component.BodyTypeName == "LinkTestApiResource");

        linkTestComponent.Description.Should().Be("A test resource used for link population integration tests.");
    }

    [Fact]
    public void When_body_type_has_no_description_attribute_then_schema_component_description_is_null()
    {
        var app = new IntegrationTestBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);

        var componentContext = OpenApiSchemaComponentRenderingContext.Create(schemaContext);
        var guidComponent = componentContext.Components.Single(static component => component.BodyTypeName == "Guid");

        guidComponent.Description.Should().BeNull();
    }

    private sealed class NoSerializerContextBlueprintApp : BlueprintApp;

    private sealed class PropertyMetadataSchemaComponentBlueprintApp : BlueprintApp
    {
        public PropertyMetadataSchemaComponentBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(PropertyMetadataSchemaComponentOperationShell), "SchemaPropertyComponentMetadata", "test-fixture");
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([typeof(SchemaPropertyMetadataResponse).FullName!]));

            this.AppOwnedSerializerContext = new SchemaPropertyMetadataJsonContext(new JsonSerializerOptions());
            this.AppOwnedSerializerContextType = typeof(SchemaPropertyMetadataJsonContext);
            this.AppOwnedSerializerContextBodyTypes = [typeof(SchemaPropertyMetadataResponse)];
            this.AppOwnedSerializerContextBodyTypeNames = [typeof(SchemaPropertyMetadataResponse).FullName!];

            this.Register(descriptor);
        }
    }

    private sealed class PropertyMetadataSchemaComponentOperationShell;
}
