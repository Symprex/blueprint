using Blueprint.SourceGenerator.Benchmarks.RuntimeModel;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Scale;

public class RuntimeBenchmarkScenarioFactoryTests
{
    [Fact]
    public void When_creating_runtime_benchmark_descriptors_then_operation_names_follow_synthetic_fixture_generator()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 3));
        var expectedOperationNames = fixture.SourceFiles
            .Where(static sourceFile => sourceFile.Path.StartsWith(@"Operations\", StringComparison.Ordinal))
            .Select(static sourceFile => Path.GetFileNameWithoutExtension(sourceFile.Path))
            .ToArray();

        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(operationCount: 3);

        descriptors.Select(static descriptor => descriptor.Name)
            .Should()
            .Equal(expectedOperationNames);
    }
}
