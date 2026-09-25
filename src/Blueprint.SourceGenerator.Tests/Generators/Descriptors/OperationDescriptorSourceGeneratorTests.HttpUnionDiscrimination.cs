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
    public void When_http_exposed_operation_declares_union_with_validation_failure_then_generator_keeps_http_union_discrimination_diagnostic_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Middleware;
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/union-with-unsupported-member")]
            public partial class UnionWithUnsupportedMemberOperation : IQuery<Results<OkResult<string>, ValidationFailedOperationResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108")
            .And.NotContain("BP110");

        result.Output.Select(static tree => tree.ToString())
            .Should()
            .Contain(static source => source.Contains("UnionWithUnsupportedMemberOperationDescriptor"));
    }

    // Not legacy-derived (KeyOnlyResourceEventResult is new-only). The host writes a key-only event as an empty 201, 200
    // or 204 by its change type (HttpOperationResultExecutor's MapChangeTypeToStatusCode), so the unions below are judged
    // against all three statuses, not 201 alone.
    [Theory]
    [InlineData("NoResultOperationResult")]
    [InlineData("Blueprint.Runtime.Http.HttpNoContentResult")]
    public void When_http_exposed_operation_declares_key_only_resource_event_and_another_empty_result_at_one_of_its_statuses_then_generator_reports_host_translation_ambiguity(string otherMember)
    {
        // An updated key-only event is an empty 200, as a NoResultOperationResult is; a deleted one an empty 204, as an
        // HttpNoContentResult is: the client cannot tell them apart.
        var result = OperationDescriptorSourceGeneratorTests.Compile($$"""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/key-only-resource-event-with-empty-result")]
            public partial class KeyOnlyResourceEventAndEmptyResultOperation : IQuery<Results<KeyOnlyResourceEventResult, {{otherMember}}>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Contain("BP109");
    }

    [Fact]
    public void When_http_exposed_operation_declares_key_only_resource_event_and_ok_union_then_generator_reports_http_union_discrimination_diagnostic()
    {
        // An updated key-only event is an empty 200 beside the OK result's body-bearing 200.
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/key-only-resource-event-with-ok")]
            public partial class KeyOnlyResourceEventAndOkOperation : IQuery<Results<KeyOnlyResourceEventResult, OkResult<string>>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Contain("BP110");
    }

    [Fact]
    public void When_http_exposed_operation_declares_key_only_resource_event_and_a_result_at_none_of_its_statuses_then_generator_keeps_both_diagnostics_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/key-only-resource-event-with-not-found")]
            public partial class KeyOnlyResourceEventAndNotFoundOperation : IQuery<Results<KeyOnlyResourceEventResult, NotFoundResult<string>>> {}
            """);

        var diagnosticIds = result.Diagnostics.Select(static diagnostic => diagnostic.Id).ToArray();
        diagnosticIds.Should().NotContain("BP109");
        diagnosticIds.Should().NotContain("BP110");
        diagnosticIds.Should().NotContain("CS0535");
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

    [Fact]
    public void When_http_exposed_operation_declares_same_status_body_bearing_resource_event_union_with_compile_time_event_ids_then_generator_does_not_report_http_union_discrimination_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;

            [ResourceEvent("events.created")]
            public sealed class CreatedPayload
            {
                public string ResourceId { get; init; } = string.Empty;
            }

            [ResourceEvent("events.updated")]
            public sealed class UpdatedPayload
            {
                public string ResourceId { get; init; } = string.Empty;
            }

            [RootLink("/host-aware/body-resource-event-union")]
            public partial class BodyResourceEventUnionOperation : IQuery<Results<ResourceEventResult<CreatedPayload>, ResourceEventResult<UpdatedPayload>>> {}
            """);

        var diagnosticIds = result.Diagnostics.Select(static diagnostic => diagnostic.Id).ToArray();
        diagnosticIds.Should().NotContain("BP109");
        diagnosticIds.Should().NotContain("BP110");
        diagnosticIds.Should().NotContain("CS0535");
    }

    [Fact]
    public void When_http_exposed_operation_declares_accepted_and_ok_union_then_generator_keeps_status_based_diagnostics_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/accepted-and-ok-union")]
            public partial class AcceptedAndOkUnionOperation : IQuery<Results<AcceptedResult<string>, OkResult<string>>> {}
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
    public void When_http_exposed_operation_declares_conflict_and_ok_union_then_generator_keeps_status_based_diagnostics_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/conflict-and-ok-union")]
            public partial class ConflictAndOkUnionOperation : IQuery<Results<ConflictResult<string>, OkResult<string>>> {}
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
    public void When_http_exposed_operation_declares_not_found_and_ok_union_then_generator_keeps_status_based_diagnostics_clear()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/not-found-and-ok-union")]
            public partial class NotFoundAndOkUnionOperation : IQuery<Results<NotFoundResult<string>, OkResult<string>>> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP109")
            .And.NotContain("BP110");
    }
}
