using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Scale;

public class SyntheticBuildTelemetryReportTests
{
    private const string SnapshotsFolder = "ScaleSnapshots/";

    [Fact]
    public async Task When_compiling_tier2_fixture_then_build_telemetry_report_serializes_cold_build_summary()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 100));
        var result = CompileResult(fixture);

        var report = SyntheticBuildTelemetryReport.Create(
            fixture,
            captureName: "cold-build",
            isDesignTimeBuild: false,
            duration: TimeSpan.FromMilliseconds(1500),
            result: result);

        report.ArtifactName.Should().Be("build-telemetry");
        report.RelativePath.Should().Be(".blueprint/manifests/build-telemetry.manifest.json");

        var payload = (JObject)report.Sections["synthetic-build-harness"].Payload;
        ((JObject)payload["scenario"]!)["operationCount"]!.Value<int>().Should().Be(100);
        ((JObject)payload["capture"]!)["durationMs"]!.Value<long>().Should().Be(1500);
        ((JObject)((JObject)payload["generators"]!)[nameof(OperationExecutorPipelineSourceGenerator)]!)["generatedOutputCount"]!
            .Value<int>()
            .Should()
            .Be(101);

        await Verify(report.ToJson())
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public async Task When_compiling_single_edit_scenario_then_build_telemetry_report_serializes_mutation_summary()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 100));
        var updatedFixture = fixture.MutateOperation(
            operationIndex: 50,
            mutation: static source => source.Replace(
                "public static global::System.Threading.Tasks.Task<string> InvokeAsync()",
                "public static global::System.Threading.Tasks.Task<string> InvokeAsync(global::Blueprint.Runtime.Exec.ApiOperationContext context)",
                StringComparison.Ordinal));
        var editResult = CompileEditResult(fixture, updatedFixture);

        var report = SyntheticBuildTelemetryReport.Create(
            updatedFixture,
            captureName: "single-edit-build",
            isDesignTimeBuild: false,
            duration: TimeSpan.FromMilliseconds(620),
            result: editResult.Updated,
            mutationKind: "single-operation",
            mutationSourcePath: @"Operations\SyntheticOperation050.cs");

        var payload = (JObject)report.Sections["synthetic-build-harness"].Payload;
        ((JObject)payload["capture"]!)["captureName"]!.Value<string>().Should().Be("single-edit-build");
        ((JObject)payload["mutation"]!)["sourcePath"]!.Value<string>().Should().Be(@"Operations\SyntheticOperation050.cs");

        await Verify(report.ToJson())
            .UseDirectory(SnapshotsFolder);
    }

    private static MultiGeneratorGenerationResult CompileResult(SyntheticBlueprintFixture fixture)
    {
        return TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
            ],
            fixture.SourceFiles.Select(static file => file.Content).ToArray(),
            fixture.SourceFiles.Select(static file => file.Path).ToArray());
    }

    private static MultiGeneratorEditResult CompileEditResult(
        SyntheticBlueprintFixture fixture,
        SyntheticBlueprintFixture updatedFixture)
    {
        return TestHelpers.CompileWithTrackedGeneratorsAfterEdit(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
            ],
            fixture.SourceFiles.Select(static file => file.Content).ToArray(),
            updatedFixture.SourceFiles.Select(static file => file.Content).ToArray(),
            fixture.SourceFiles.Select(static file => file.Path).ToArray());
    }
}
