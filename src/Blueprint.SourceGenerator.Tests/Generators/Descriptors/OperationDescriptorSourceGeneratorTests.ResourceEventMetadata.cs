using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsResourceEventMetadata
{
    [Fact]
    public void When_http_operation_declares_body_bearing_resource_event_payload_with_event_id_attribute_then_generated_descriptor_sets_resource_event_metadata_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            [ResourceEvent("events.created")]
            public sealed class CreatedPayload
            {
                public string ResourceId { get; init; } = string.Empty;
            }

            [RootLink("/resource-events/created")]
            public partial class CreatedResourceEventOperation : IQuery<ResourceEventResult<CreatedPayload>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("CreatedResourceEventOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("OperationResourceEventMetadataFeatureData");
        descriptorSource.Should().Contain("\"events.created\"");
        descriptorSource.Should().Contain("\"TestNamespace.CreatedPayload\"");
        descriptorSource.Should().Contain("\"Blueprint.Runtime.Exec.ResourceEventResult<TestNamespace.CreatedPayload>\"");
    }

    [Fact]
    public void When_http_operation_declares_body_bearing_resource_event_payload_without_event_id_attribute_then_generated_descriptor_does_not_set_resource_event_metadata_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            public sealed class CreatedPayload
            {
                public string ResourceId { get; init; } = string.Empty;
            }

            [RootLink("/resource-events/created")]
            public partial class CreatedResourceEventOperation : IQuery<ResourceEventResult<CreatedPayload>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("CreatedResourceEventOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().NotContain("OperationResourceEventMetadataFeatureData");
    }
}
