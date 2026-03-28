using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsHostExposureTests
{
    [Fact]
    public void When_root_link_present_then_generated_descriptor_sets_http_host_exposure_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [global::Blueprint.Model.Attributes.RootLink("/host-aware/test")]
            public partial class HttpExposedOperation : IOperation {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("HttpExposedOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(new global::Blueprint.Model.OperationHostExposureFeatureData(global::System.Collections.Immutable.ImmutableArray.Create(\"http\")));");
    }

    [Fact]
    public void When_background_task_operation_present_then_generated_descriptor_sets_tasks_host_exposure_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Tasks;

            public partial class SendWelcomeEmailTask : IBackgroundTask {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("SendWelcomeEmailTaskDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(new global::Blueprint.Model.OperationHostExposureFeatureData(global::System.Collections.Immutable.ImmutableArray.Create(\"tasks\")));");
    }
}
