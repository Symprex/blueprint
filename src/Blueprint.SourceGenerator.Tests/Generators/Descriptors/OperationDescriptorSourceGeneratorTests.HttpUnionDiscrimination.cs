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

    [Fact]
    public void When_http_exposed_operation_declares_key_only_resource_event_and_no_result_union_then_generator_keeps_http_union_discrimination_diagnostic_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/key-only-resource-event-with-no-result")]
            public partial class KeyOnlyResourceEventAndNoResultOperation : IQuery<Results<KeyOnlyResourceEventResult, NoResultOperationResult>> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP110");
    }

    [Fact]
    public void When_http_exposed_operation_declares_created_and_ok_union_then_generator_keeps_status_based_diagnostics_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/created-and-ok-union")]
            public partial class CreatedAndOkUnionOperation : IQuery<Results<CreatedResult<string>, OkResult<string>>> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP109")
            .And.NotContain("BP110");
    }

    [Fact]
    public void When_http_exposed_operation_declares_created_and_key_only_resource_event_union_then_generator_reports_http_union_discrimination_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/created-and-key-only-resource-event-union")]
            public partial class CreatedAndKeyOnlyResourceEventUnionOperation : IQuery<Results<CreatedResult<string>, KeyOnlyResourceEventResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP110");
    }
}
