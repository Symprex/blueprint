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
            "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.HttpHostExposure);");
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

    [Fact]
    public void When_operation_is_exposed_to_http_and_tasks_hosts_then_generator_reports_conflicting_host_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Tasks;

            [global::Blueprint.Model.Attributes.RootLink("/tasks/send-welcome-email")]
            public partial class SendWelcomeEmailTask : IBackgroundTask {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BPOD002");
    }
}
