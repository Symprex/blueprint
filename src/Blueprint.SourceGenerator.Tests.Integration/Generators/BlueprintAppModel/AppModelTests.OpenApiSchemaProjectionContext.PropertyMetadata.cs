using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiSchemaProjectionContextPropertyMetadataTests
{
    [Fact]
    public void When_serializer_metadata_exposes_object_properties_then_schema_projection_context_projects_property_types_and_constraints()
    {
        var app = new PropertyMetadataBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var schema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == typeof(SchemaPropertyMetadataResponse).FullName);

        schema.Kind.Should().Be(OpenApiSchemaKind.Object);
        schema.PropertyNames.Should().Equal("Child", "Count", "Enabled", "Extensions", "Id", "Metadata", "Tags");
        schema.Properties.Select(static property => property.Name).Should().Equal("Child", "Count", "Enabled", "Extensions", "Id", "Metadata", "Tags");

        var childProperty = schema.Properties.Single(static property => property.Name == "Child");
        childProperty.Kind.Should().Be(OpenApiSchemaKind.Object);
        childProperty.IsRequired.Should().BeFalse();
        childProperty.IsExtensionData.Should().BeFalse();

        var countProperty = schema.Properties.Single(static property => property.Name == "Count");
        countProperty.Kind.Should().Be(OpenApiSchemaKind.Integer);
        countProperty.IsRequired.Should().BeFalse();
        countProperty.IsExtensionData.Should().BeFalse();

        var enabledProperty = schema.Properties.Single(static property => property.Name == "Enabled");
        enabledProperty.Kind.Should().Be(OpenApiSchemaKind.Boolean);
        enabledProperty.IsRequired.Should().BeFalse();
        enabledProperty.IsExtensionData.Should().BeFalse();

        var extensionsProperty = schema.Properties.Single(static property => property.Name == "Extensions");
        extensionsProperty.Kind.Should().Be(OpenApiSchemaKind.Object);
        extensionsProperty.IsRequired.Should().BeFalse();
        extensionsProperty.IsExtensionData.Should().BeTrue();

        var idProperty = schema.Properties.Single(static property => property.Name == "Id");
        idProperty.Kind.Should().Be(OpenApiSchemaKind.String);
        idProperty.IsRequired.Should().BeTrue();
        idProperty.IsExtensionData.Should().BeFalse();

        var metadataProperty = schema.Properties.Single(static property => property.Name == "Metadata");
        metadataProperty.Kind.Should().Be(OpenApiSchemaKind.Object);
        metadataProperty.IsRequired.Should().BeFalse();
        metadataProperty.IsExtensionData.Should().BeFalse();

        var tagsProperty = schema.Properties.Single(static property => property.Name == "Tags");
        tagsProperty.Kind.Should().Be(OpenApiSchemaKind.Array);
        tagsProperty.IsRequired.Should().BeFalse();
        tagsProperty.IsExtensionData.Should().BeFalse();
    }

    [Fact]
    public void When_serializer_metadata_and_validation_attributes_expose_constraints_then_schema_projection_context_projects_supported_constraint_metadata()
    {
        var app = new ValidationConstraintBlueprintApp();
        var documentContext = OpenApiDocumentBuilderContext.Create(app);

        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var schema = schemaContext.Schemas.Single(static schema => schema.BodyTypeName == typeof(SchemaValidationConstraintsResponse).FullName);

        var codeProperty = schema.Properties.Single(static property => property.Name == "Code");
        codeProperty.MinLength.Should().Be(4);
        codeProperty.MaxLength.Should().Be(12);
        codeProperty.Pattern.Should().Be("^[A-Z0-9]+$");

        var positiveCountProperty = schema.Properties.Single(static property => property.Name == "PositiveCount");
        positiveCountProperty.Minimum.Should().Be(0m);
        positiveCountProperty.IsMinimumExclusive.Should().BeTrue();
        positiveCountProperty.Maximum.Should().BeNull();

        var quantityProperty = schema.Properties.Single(static property => property.Name == "Quantity");
        quantityProperty.Minimum.Should().Be(1m);
        quantityProperty.Maximum.Should().Be(10m);
        quantityProperty.IsMinimumExclusive.Should().BeFalse();
        quantityProperty.IsMaximumExclusive.Should().BeFalse();
    }

    private sealed class PropertyMetadataBlueprintApp : BlueprintApp
    {
        public PropertyMetadataBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(PropertyMetadataOperationShell), "SchemaPropertyMetadata", "test-fixture");
            descriptor.SetFeatureData(new OperationHostExposureFeatureData(["http"]));
            descriptor.SetFeatureData(new OperationSerializerContextCoverageFeatureData([typeof(SchemaPropertyMetadataResponse).FullName!]));

            this.AppOwnedSerializerContext = new SchemaPropertyMetadataJsonContext(new JsonSerializerOptions());
            this.AppOwnedSerializerContextType = typeof(SchemaPropertyMetadataJsonContext);
            this.AppOwnedSerializerContextBodyTypes = [typeof(SchemaPropertyMetadataResponse)];
            this.AppOwnedSerializerContextBodyTypeNames = [typeof(SchemaPropertyMetadataResponse).FullName!];

            this.Register(descriptor);
        }
    }

    private sealed class PropertyMetadataOperationShell;
}

public sealed class SchemaPropertyMetadataResponse
{
    public SchemaPropertyMetadataChild? Child { get; init; }

    public int Count { get; init; }

    public bool Enabled { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }

    public required string Id { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);

    public List<string> Tags { get; init; } = [];
}

public sealed class SchemaPropertyMetadataChild
{
    public bool Enabled { get; init; }
}

[JsonSerializable(typeof(SchemaPropertyMetadataResponse))]
public partial class SchemaPropertyMetadataJsonContext : JsonSerializerContext;
