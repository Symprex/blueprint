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
    public void When_http_exposed_operation_declares_union_with_missing_executor_member_then_generator_keeps_missing_executor_diagnostic_distinct()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Middleware;
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/missing-executor-member")]
            public partial class MissingExecutorMemberOperation : IQuery<Results<OkResult<string>, ValidationFailedOperationResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP108")
            .And.NotContain("BP109");
    }
}
