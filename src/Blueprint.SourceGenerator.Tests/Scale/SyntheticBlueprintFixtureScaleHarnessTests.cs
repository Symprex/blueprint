using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Scale;

public class SyntheticBlueprintFixtureScaleHarnessTests
{
    [Fact]
    public void When_generating_tier2_fixture_then_multi_generator_harness_supports_descriptor_app_and_pipeline_generators()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 100));

        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
            ],
            fixture.SourceFiles.Select(static file => file.Content).ToArray(),
            fixture.SourceFiles.Select(static file => file.Path).ToArray());

        result.Diagnostics.Should().BeEmpty();
        result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator))
            .Count(static source => source.HintName.EndsWith("_Descriptor.g.cs", StringComparison.Ordinal))
            .Should()
            .Be(100);
        result.GetGeneratedSources(nameof(AppDataModelSourceGenerator))
            .Count(static source => source.HintName.EndsWith("_DataModelRegistration.g.cs", StringComparison.Ordinal))
            .Should()
            .Be(1);
        result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator))
            .Count(static source => source.HintName.EndsWith("ExecutorPipeline.g.cs", StringComparison.Ordinal))
            .Should()
            .Be(100);
    }

    [Fact]
    public void When_mutating_single_operation_then_generated_outputs_stay_localized()
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

        var editResult = TestHelpers.CompileWithTrackedGeneratorsAfterEdit(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
            ],
            fixture.SourceFiles.Select(static file => file.Content).ToArray(),
            updatedFixture.SourceFiles.Select(static file => file.Content).ToArray(),
            fixture.SourceFiles.Select(static file => file.Path).ToArray());

        editResult.Updated.Diagnostics.Should().BeEmpty();
        editResult.GetChangedGeneratedSourceHintNames(nameof(OperationDescriptorSourceGenerator))
            .Should()
            .BeEmpty();
        editResult.GetChangedGeneratedSourceHintNames(nameof(AppDataModelSourceGenerator))
            .Should()
            .BeEmpty();
        editResult.GetChangedGeneratedSourceHintNames(nameof(OperationExecutorPipelineSourceGenerator))
            .Should()
            .Equal("SyntheticOperation050ExecutorPipeline.g.cs");
    }

    [Fact]
    public void When_mutating_shared_validation_contract_then_pipeline_output_changes_are_broader_but_explainable()
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

        var editResult = TestHelpers.CompileWithTrackedGeneratorsAfterEdit(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<AppDataModelSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
            ],
            fixture.SourceFiles.Select(static file => file.Content).ToArray(),
            updatedFixture.SourceFiles.Select(static file => file.Content).ToArray(),
            fixture.SourceFiles.Select(static file => file.Path).ToArray());

        editResult.Updated.Diagnostics.Should().BeEmpty();
        editResult.GetChangedGeneratedSourceHintNames(nameof(OperationDescriptorSourceGenerator))
            .Should()
            .BeEmpty();
        editResult.GetChangedGeneratedSourceHintNames(nameof(AppDataModelSourceGenerator))
            .Should()
            .BeEmpty();
        editResult.GetChangedGeneratedSourceHintNames(nameof(OperationExecutorPipelineSourceGenerator))
            .Should()
            .Equal(Enumerable.Range(1, 100).Select(static operationIndex => $"SyntheticOperation{operationIndex:000}ExecutorPipeline.g.cs"));
    }
}
