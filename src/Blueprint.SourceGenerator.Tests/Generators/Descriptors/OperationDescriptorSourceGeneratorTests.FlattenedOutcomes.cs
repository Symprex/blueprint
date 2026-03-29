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
}
