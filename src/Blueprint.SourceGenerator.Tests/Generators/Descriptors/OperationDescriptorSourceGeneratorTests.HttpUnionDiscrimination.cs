using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsHttpUnionDiscriminationTests
{
    [Fact]
    public void When_http_exposed_operation_declares_same_status_union_without_explicit_discrimination_strategy_then_generator_reports_http_union_discrimination_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/same-status-union-without-discriminator")]
            public partial class SameStatusUnionWithoutDiscriminatorOperation : IQuery<Results<OkResult<string>, NoResultOperationResult>> {}
            """);

        result.Diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BP110");
    }

    [Fact]
    public void When_http_exposed_operation_declares_same_shape_union_then_generator_keeps_host_translation_ambiguity_diagnostic_distinct()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/same-shape-union")]
            public partial class SameShapeUnionOperation : IQuery<Results<OkResult<string>, OkResult<int>>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP109")
            .And.NotContain("BP110");
    }

    [Fact]
    public void When_http_exposed_operation_declares_union_with_missing_executor_member_then_generator_keeps_host_executor_diagnostic_distinct()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Middleware;
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/union-with-unsupported-member")]
            public partial class UnionWithUnsupportedMemberOperation : IQuery<Results<OkResult<string>, ValidationFailedOperationResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP108")
            .And.NotContain("BP110");
    }
}
