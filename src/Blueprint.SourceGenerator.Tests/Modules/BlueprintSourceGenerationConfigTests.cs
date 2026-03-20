using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

public class BlueprintSourceGenerationConfigTests
{
    [Fact]
    public void When_link_generation_module_used_then_exposes_real_contributors()
    {
        var module = new LinkGenerationModule();
        var config = new BlueprintSourceGenerationConfig([module]);

        var result = config.BuildExecutionPlan();

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.PostInitializationContributors.Should().ContainSingle().Which.Should().BeSameAs(module);
        result.Value.DescriptorContributors.Should().ContainSingle().Which.Should().BeSameAs(module);
    }

    [Fact]
    public void When_dependency_requires_reorder_then_orders_contributors_deterministically()
    {
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDescriptorModule("second", order: 0, dependsOn: ["first"]),
            new TestDescriptorModule("first", order: 100),
        ]);

        var result = config.BuildExecutionPlan();

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.DescriptorContributors
            .Select(c => c.ContributorId)
            .Should()
            .Equal("first", "second");
    }

    [Fact]
    public void When_duplicate_unique_descriptor_contributors_then_reports_diagnostic()
    {
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDescriptorModule("links", isUnique: true),
            new TestDescriptorModule("links", isUnique: true),
        ]);

        var result = config.BuildExecutionPlan();

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BPMOD001");
    }

    [Fact]
    public void When_dependency_missing_then_reports_diagnostic()
    {
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDescriptorModule("worker", dependsOn: ["missing"]),
        ]);

        var result = config.BuildExecutionPlan();

        result.Value.Should().BeNull();
        result.Diagnostics.Select(d => d.ToDiagnostic().Id).Should().Contain("BPMOD002");
    }

    [Fact]
    public void When_dependency_cycle_exists_then_reports_diagnostic()
    {
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDescriptorModule("first", dependsOn: ["second"]),
            new TestDescriptorModule("second", dependsOn: ["first"]),
        ]);

        var result = config.BuildExecutionPlan();

        result.Value.Should().BeNull();
        result.Diagnostics.Select(d => d.ToDiagnostic().Id).Should().Contain("BPMOD003");
    }

    [Fact]
    public void When_core_and_consumer_modules_share_order_then_core_runs_first()
    {
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDescriptorModule("consumer", category: BlueprintModuleCategory.Consumer),
            new TestDescriptorModule("core", category: BlueprintModuleCategory.Core),
        ]);

        var result = config.BuildExecutionPlan();

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.DescriptorContributors
            .Select(c => c.ContributorId)
            .Should()
            .Equal("core", "consumer");
    }

    [Fact]
    public void When_equivalent_execution_plans_built_then_they_compare_equal()
    {
        var first = new BlueprintSourceGenerationConfig([new LinkGenerationModule()]).BuildExecutionPlan();
        var second = new BlueprintSourceGenerationConfig([new LinkGenerationModule()]).BuildExecutionPlan();

        first.Diagnostics.Should().BeEmpty();
        second.Diagnostics.Should().BeEmpty();
        first.Value.Should().Be(second.Value);
    }

    [Fact]
    public void When_dependency_targets_non_unique_contributor_id_then_waits_for_all_matches()
    {
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestDescriptorModule("helper", order: 0),
            new TestDescriptorModule("worker", order: 0, dependsOn: ["helper"]),
            new TestDescriptorModule("helper", order: 100),
        ]);

        var result = config.BuildExecutionPlan();

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.DescriptorContributors
            .Select(c => c.ContributorId)
            .Should()
            .Equal("helper", "helper", "worker");
    }

    private sealed class TestDescriptorModule(
        string contributorId,
        int order = 0,
        bool isUnique = false,
        BlueprintModuleCategory category = BlueprintModuleCategory.Core,
        params string[] dependsOn)
        : IBlueprintSourceGeneratorModule, IDescriptorContributor
    {
        public string ModuleId => this.ContributorId + "-module";

        public BlueprintModuleCategory Category { get; } = category;

        public string ContributorId { get; } = contributorId;

        public int Order { get; } = order;

        public bool IsUnique { get; } = isUnique;

        public IReadOnlyList<string> DependsOn { get; } = dependsOn;

        public void ContributeToDescriptor(DescriptorContributorContext context)
        {
        }
    }
}
