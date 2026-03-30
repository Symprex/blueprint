using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsNoResultShapeSeparationTests
{
    [Fact]
    public void When_http_exposed_operation_declares_host_neutral_and_http_owned_no_result_shapes_in_one_union_then_generator_reports_no_result_shape_separation_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;
            using Blueprint.Runtime.Http;

            [RootLink("/host-aware/no-result-shape-conflict")]
            public partial class NoResultShapeConflictOperation : IQuery<Results<NoResultOperationResult, HttpNoContentResult>> {}
            """);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP112");
    }

    [Fact]
    public void When_http_exposed_operation_declares_the_same_no_result_shape_union_in_reverse_order_then_generator_still_reports_no_result_shape_separation_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;
            using Blueprint.Runtime.Http;

            [RootLink("/host-aware/no-result-shape-conflict-reversed")]
            public partial class NoResultShapeConflictReversedOperation : IQuery<Results<HttpNoContentResult, NoResultOperationResult>> {}
            """);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP112");
    }

    [Fact]
    public void When_http_exposed_operation_declares_single_host_neutral_no_result_shape_then_generator_does_not_report_no_result_shape_separation_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/single-no-result")]
            public partial class SingleNoResultOperation : IQuery<NoResultOperationResult> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP112");
    }

    [Fact]
    public void When_http_exposed_operation_declares_single_http_owned_no_content_shape_then_generator_does_not_report_no_result_shape_separation_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Http;

            [RootLink("/host-aware/single-http-no-content")]
            public partial class SingleHttpNoContentOperation : IQuery<HttpNoContentResult> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP112");
    }

    [Fact]
    public void When_http_exposed_operation_declares_same_status_union_that_requires_discrimination_then_generator_keeps_no_result_shape_separation_diagnostic_distinct()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/same-status-union")]
            public partial class SameStatusUnionOperation : IQuery<Results<OkResult<string>, NoResultOperationResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP110")
            .And.NotContain("BP112");
    }

    [Fact]
    public void When_http_exposed_operation_declares_union_with_validation_failure_then_generator_keeps_no_result_shape_separation_diagnostic_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Middleware;
            using Blueprint.Runtime.Http;

            [RootLink("/host-aware/missing-executor-member")]
            public partial class MissingExecutorMemberOperation : IQuery<Results<HttpNoContentResult, ValidationFailedOperationResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108")
            .And.NotContain("BP112");

        result.Output.Select(static tree => tree.ToString())
            .Should()
            .Contain(static source => source.Contains("MissingExecutorMemberOperationDescriptor"));
    }
}
