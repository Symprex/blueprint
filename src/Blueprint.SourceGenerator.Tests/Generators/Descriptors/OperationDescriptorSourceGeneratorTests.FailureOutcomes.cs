using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsFailureOutcomesTests
{
    [Fact]
    public void When_pipeline_uses_default_exception_translators_then_generated_descriptor_sets_failure_outcomes_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            public partial class IncludedOperation : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("IncludedOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("this.SetFeatureData(new global::Blueprint.Model.OperationFailureOutcomesFeatureData(");
        descriptorSource.Should().Contain("new global::Blueprint.Model.OperationFailureOutcome(\"legacy-validation-exception\", \"Blueprint.Validation.ValidationException\", \"Blueprint.Runtime.Exec.ValidationFailedResult\")");
        descriptorSource.Should().Contain("new global::Blueprint.Model.OperationFailureOutcome(\"data-annotations-validation-exception\", \"System.ComponentModel.DataAnnotations.ValidationException\", \"Blueprint.Runtime.Exec.ValidationFailedResult\")");
        descriptorSource.Should().Contain("new global::Blueprint.Model.OperationFailureOutcome(\"default-unhandled-exception\", \"System.Exception\", \"Blueprint.Runtime.Exec.UnhandledExceptionResult\")");
    }
}
