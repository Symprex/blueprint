using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsFlattenedOutcomesTests
{
    [Fact]
    public void When_declared_contract_is_results_union_then_generated_descriptor_sets_flattened_outcome_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            public partial class IncludedOperation : IQuery<Results<OkResult<string>, NoResultOperationResult>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("IncludedOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("this.SetFeatureData(new global::Blueprint.Model.OperationResultOutcomesFeatureData(");
        descriptorSource.Should().Contain("typeof(global::Blueprint.Runtime.Exec.OkResult<string>)");
        descriptorSource.Should().Contain("typeof(global::Blueprint.Runtime.Exec.NoResultOperationResult)");
    }

    [Fact]
    public void When_declared_contract_contains_non_semantic_union_member_then_generator_reports_outcome_alignment_diagnostic_and_skips_flattened_outcome_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            public partial class InvalidFlattenedOutcomeOperation : IQuery<Results<OkResult<string>, string>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP111");

        result.Output.Select(static tree => tree.ToString())
            .Should()
            .NotContain(static source => source.Contains("InvalidFlattenedOutcomeOperationDescriptor"));
    }

    [Fact]
    public void When_declared_contract_contains_key_only_resource_event_result_then_generated_descriptor_keeps_it_in_flattened_outcome_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            public partial class KeyOnlyResourceEventOperation : IQuery<Results<KeyOnlyResourceEventResult, NoResultOperationResult>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("KeyOnlyResourceEventOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("typeof(global::Blueprint.Runtime.Exec.KeyOnlyResourceEventResult)");
        descriptorSource.Should().Contain("typeof(global::Blueprint.Runtime.Exec.NoResultOperationResult)");
    }
}
