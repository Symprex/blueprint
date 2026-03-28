using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsDiscoveryFeatureDataTests
{
    [Fact]
    public void When_IOperation_then_generated_descriptor_sets_discovery_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            public partial class IncludedOperation : IOperation {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("IncludedOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(new global::Blueprint.Model.OperationDiscoveryFeatureData(\"operation\", \"TestNamespace\", \"IncludedOperation\"));");
    }

    [Fact]
    public void When_ICommand_then_generated_descriptor_sets_command_kind()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            public partial class CreateUserCommand : ICommand {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("CreateUserCommandDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("\"command\"");
    }

    [Fact]
    public void When_IQuery_then_generated_descriptor_sets_query_kind()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            public partial class GetUserQuery : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static t => t.ToString())
            .FirstOrDefault(static s => s.Contains("GetUserQueryDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("\"query\"");
    }
}
