using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsAmbiguousHostTranslationTests
{
    [Fact]
    public void When_http_exposed_operation_declares_same_status_success_union_then_generator_reports_ambiguous_host_translation_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/ambiguous-success-union")]
            public partial class AmbiguousHostTranslationOperation : IQuery<Results<OkResult<string>, OkResult<int>>> {}
            """);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP109");
    }

    [Fact]
    public void When_http_exposed_operation_declares_single_supported_semantic_result_then_generator_does_not_report_ambiguous_host_translation_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/non-ambiguous-single-result")]
            public partial class NonAmbiguousSingleResultOperation : IQuery<OkResult<string>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP109");
    }

    [Fact]
    public void When_http_exposed_operation_declares_union_with_validation_failure_then_generator_keeps_ambiguous_host_translation_diagnostic_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Middleware;
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/missing-executor-member")]
            public partial class MissingExecutorMemberOperation : IQuery<Results<OkResult<string>, ValidationFailedOperationResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108")
            .And.NotContain("BP109");

        result.Output.Select(static tree => tree.ToString())
            .Should()
            .Contain(static source => source.Contains("MissingExecutorMemberOperationDescriptor"));
    }
}
