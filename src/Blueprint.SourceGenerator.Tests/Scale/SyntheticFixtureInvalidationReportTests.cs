using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Scale;

public class SyntheticFixtureInvalidationReportTests
{
    private const string SnapshotsFolder = "ScaleSnapshots/";

    [Fact]
    public async Task When_mutating_single_operation_then_invalidation_report_serializes_localized_change_summary()
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

        var report = SyntheticFixtureInvalidationReport.Create(
            fixture,
            mutationKind: "single-operation",
            mutationSourcePath: @"Operations\SyntheticOperation050.cs",
            editResult: editResult);

        report.ArtifactName.Should().Be("scale-invalidation");
        report.RelativePath.Should().Be(".blueprint/manifests/scale-invalidation.manifest.json");

        var payload = (JObject)report.Sections["synthetic-scale-harness"].Payload;
        ((JObject)payload["scenario"]!)["operationCount"]!.Value<int>().Should().Be(100);
        ((JObject)payload["mutation"]!)["kind"]!.Value<string>().Should().Be("single-operation");
        ((JObject)((JObject)payload["generators"]!)[nameof(OperationExecutorPipelineSourceGenerator)]!)["changedOutputs"]!
            .Values<string>()
            .Should()
            .Equal("Synthetic.Scale.SyntheticOperation050ExecutorPipeline.g.cs");

        await Verify(report.ToJson())
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public async Task When_mutating_shared_contract_then_invalidation_report_serializes_broad_pipeline_change_summary()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 100));
        var updatedFixture = fixture.MutateSourceFile(
            path: @"Shared\SyntheticOperationBase.cs",
            mutation: static source => source.Replace(
                "[global::System.ComponentModel.DataAnnotations.Required]",
                "[global::System.ComponentModel.DataAnnotations.Required]\n        [global::System.ComponentModel.DataAnnotations.MaxLength(5)]",
                StringComparison.Ordinal));
        var editResult = CompileEditResult(fixture, updatedFixture);

        var report = SyntheticFixtureInvalidationReport.Create(
            fixture,
            mutationKind: "shared-contract",
            mutationSourcePath: @"Shared\SyntheticOperationBase.cs",
            editResult: editResult);

        var payload = (JObject)report.Sections["synthetic-scale-harness"].Payload;
        ((JObject)payload["mutation"]!)["sourcePath"]!.Value<string>().Should().Be(@"Shared\SyntheticOperationBase.cs");
        ((JObject)((JObject)payload["generators"]!)[nameof(OperationExecutorPipelineSourceGenerator)]!)["changedOutputs"]!
            .Values<string>()
            .Should()
            .HaveCount(100);

        await Verify(report.ToJson())
            .UseDirectory(SnapshotsFolder);
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
