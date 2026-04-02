using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorOperationGroupMetadataTests
{
    [Fact]
    public void When_http_descriptor_is_configured_then_operation_group_metadata_uses_the_operation_namespace_segment()
    {
        var data = HttpResponseMetadataQueryOp.Configure().GetFeatureData<OperationGroupMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.GroupName.Should().Be("OperationDescriptors");
    }
}
