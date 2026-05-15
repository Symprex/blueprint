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
            .Equal("Synthetic.Scale.SyntheticOperation050ExecutorPipeline.g.cs");
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
            .Equal(Enumerable.Range(1, 100).Select(static operationIndex => $"Synthetic.Scale.SyntheticOperation{operationIndex:000}ExecutorPipeline.g.cs"));
    }

    [Fact]
    public void When_generating_link_heavy_fixture_then_descriptor_output_contains_root_links_for_each_operation()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 100,
                includeRootLinks: true));

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
            .Single(static source => source.HintName == "Synthetic.Scale.SyntheticOperation050_Descriptor.g.cs")
            .SourceText
            .ToString()
            .Should()
            .Contain("/synthetic/operations/050/{Id}");
    }

    [Fact]
    public void When_mutating_single_root_link_in_link_heavy_fixture_then_descriptor_output_changes_stay_localized()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Scale",
                appName: "SyntheticScaleApp",
                operationCount: 100,
                includeRootLinks: true));
        var updatedFixture = fixture.MutateOperation(
            operationIndex: 50,
            mutation: static source => source.Replace(
                "/synthetic/operations/050/{Id}",
                "/synthetic/operations/050/alternate/{Id}",
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
            .Equal("Synthetic.Scale.SyntheticOperation050_Descriptor.g.cs");
        editResult.GetChangedGeneratedSourceHintNames(nameof(AppDataModelSourceGenerator))
            .Should()
            .BeEmpty();
        editResult.GetChangedGeneratedSourceHintNames(nameof(OperationExecutorPipelineSourceGenerator))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void When_generating_tier3_fixture_at_500_operations_then_all_generators_compile_cleanly()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Tier3",
                appName: "SyntheticTier3App",
                operationCount: 500,
                includeRootLinks: true));

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
            .Be(500);
        result.GetGeneratedSources(nameof(AppDataModelSourceGenerator))
            .Count(static source => source.HintName.EndsWith("_DataModelRegistration.g.cs", StringComparison.Ordinal))
            .Should()
            .Be(1);
        result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator))
            .Count(static source => source.HintName.EndsWith("ExecutorPipeline.g.cs", StringComparison.Ordinal))
            .Should()
            .Be(500);
    }

    [Fact]
    public void When_mutating_single_operation_in_tier3_fixture_then_only_that_pipeline_changes()
    {
        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: "Synthetic.Tier3",
                appName: "SyntheticTier3App",
                operationCount: 500,
                includeRootLinks: true));
        var updatedFixture = fixture.MutateOperation(
            operationIndex: 250,
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
            .Equal("Synthetic.Tier3.SyntheticOperation250ExecutorPipeline.g.cs");
    }
}
