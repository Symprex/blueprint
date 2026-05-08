using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsOperationGroupMetadataTests
{
    [Fact]
    public void When_http_operation_is_generated_then_descriptor_sets_operation_group_metadata_from_namespace()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/operation-group/default-query")]
            public partial class DefaultQueryOperation : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("DefaultQueryOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(new global::Blueprint.Model.OperationGroupMetadataFeatureData(\"TestNamespace\"));");
    }

    [Fact]
    public void When_http_operation_declares_apioperations_group_then_descriptor_sets_operation_group_metadata_from_attribute()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [ApiOperations("Billing")]
            [RootLink("/operation-group/explicit-query")]
            public partial class ExplicitGroupQueryOperation : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("ExplicitGroupQueryOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(new global::Blueprint.Model.OperationGroupMetadataFeatureData(\"Billing\"));");
    }

    [Fact]
    public void When_apioperations_has_description_then_descriptor_passes_description_to_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [ApiOperations("Billing", Description = "Billing and subscription management.")]
            [RootLink("/operation-group/described-query")]
            public partial class DescribedGroupQueryOperation : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("DescribedGroupQueryOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(new global::Blueprint.Model.OperationGroupMetadataFeatureData(\"Billing\", \"Billing and subscription management.\"));");
    }
}
