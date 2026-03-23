using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Scale;

public class SyntheticBlueprintFixtureGeneratorTests
{
    [Fact]
    public void When_generating_same_fixture_twice_then_source_files_are_stable()
    {
        var options = new SyntheticBlueprintFixtureOptions(
            namespaceName: "Synthetic.Scale",
            appName: "SyntheticScaleApp",
            operationCount: 5);

        var first = SyntheticBlueprintFixtureGenerator.Create(options);
        var second = SyntheticBlueprintFixtureGenerator.Create(options);

        first.SourceFiles.Select(static file => file.Path)
            .Should()
            .Equal(second.SourceFiles.Select(static file => file.Path));
        first.SourceFiles.Select(static file => file.Content)
            .Should()
            .Equal(second.SourceFiles.Select(static file => file.Content));
    }

    [Fact]
    public void When_generating_fixture_then_app_and_requested_operations_are_emitted()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 5));

        fixture.SourceFiles.Select(static file => file.Path)
            .Should()
            .Contain("SyntheticScaleApp.cs")
            .And.Contain(@"Shared\SyntheticOperationBase.cs")
            .And.Contain("Operations\\SyntheticOperation001.cs")
            .And.Contain("Operations\\SyntheticOperation005.cs");
        fixture.SourceFiles.Should().HaveCount(7);
    }

    [Fact]
    public void When_generating_fixture_then_combined_source_compiles_with_descriptor_generator()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 5));

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(fixture.CombinedSource);

        result.Diagnostics.Should().BeEmpty();
        result.Output.Count(static tree => tree.FilePath.Contains("SyntheticOperation", StringComparison.Ordinal))
            .Should()
            .Be(5);
    }
}
