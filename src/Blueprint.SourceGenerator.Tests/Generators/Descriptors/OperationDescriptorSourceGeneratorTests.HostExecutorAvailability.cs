using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsHostExecutorAvailabilityTests
{
    [Fact]
    public void When_http_exposed_operation_declares_legacy_validation_failed_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Middleware;

            [RootLink("/host-aware/unsupported")]
            public partial class UnsupportedHttpResultOperation : IQuery<ValidationFailedOperationResult> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");

        result.Output.Select(static tree => tree.ToString())
            .Should()
            .Contain(static source => source.Contains("UnsupportedHttpResultOperationDescriptor"));
    }

    [Fact]
    public void When_http_exposed_operation_declares_canonical_validation_failed_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/canonical-validation-failed")]
            public partial class CanonicalValidationFailedResultOperation : IQuery<ValidationFailedResult> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");

        result.Output.Select(static tree => tree.ToString())
            .Should()
            .Contain(static source => source.Contains("CanonicalValidationFailedResultOperationDescriptor"));
    }

    [Fact]
    public void When_http_exposed_operation_declares_supported_semantic_result_then_generator_does_not_report_missing_host_executor_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/supported")]
            public partial class SupportedHttpResultOperation : IQuery<OkResult<string>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
    }

    [Fact]
    public void When_http_exposed_operation_declares_created_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/created-result")]
            public partial class CreatedResultOperation : IQuery<CreatedResult<string>> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
    }

    [Fact]
    public void When_http_exposed_operation_declares_accepted_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/accepted-result")]
            public partial class AcceptedResultOperation : IQuery<AcceptedResult<string>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void When_http_exposed_operation_declares_conflict_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/conflict-result")]
            public partial class ConflictResultOperation : IQuery<ConflictResult<string>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void When_http_exposed_operation_declares_not_found_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/not-found-result")]
            public partial class NotFoundResultOperation : IQuery<NotFoundResult<string>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void When_http_exposed_operation_declares_key_only_resource_event_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/key-only-resource-event")]
            public partial class KeyOnlyResourceEventResultOperation : IQuery<KeyOnlyResourceEventResult> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
    }

    [Fact]
    public void When_http_exposed_operation_declares_body_bearing_resource_event_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/body-resource-event")]
            public partial class BodyResourceEventResultOperation : IQuery<ResourceEventResult<string>> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
    }

    [Fact]
    public void When_http_exposed_operation_declares_http_owned_no_content_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Http;

            [RootLink("/host-aware/http-no-content")]
            public partial class HttpNoContentResultOperation : IQuery<HttpNoContentResult> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
    }

    [Fact]
    public void When_http_exposed_operation_declares_mixed_results_union_with_validation_failure_then_generator_treats_both_members_as_supported()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Middleware;
            using Blueprint.Runtime.Exec;

            [RootLink("/host-aware/mixed-union")]
            public partial class MixedUnionHttpResultOperation : IQuery<Results<OkResult<string>, ValidationFailedOperationResult>> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");

        result.Output.Select(static tree => tree.ToString())
            .Should()
            .Contain(static source => source.Contains("MixedUnionHttpResultOperationDescriptor"));
    }

    [Fact]
    public void When_http_exposed_operation_declares_legacy_resource_event_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Http;

            public partial class TestResource : Blueprint.Model.ApiResource
            {
                public override string Object => "test-resource";
                public string Id { get; set; } = "";
            }

            [RootLink("/host-aware/legacy-resource-event")]
            public partial class LegacyResourceEventOperation : ICommand<ResourceEvent<TestResource>> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
    }

    [Fact]
    public void When_http_exposed_operation_declares_legacy_resource_updated_result_then_generator_treats_it_as_supported_by_current_http_executor()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Http;

            public partial class TestResource2 : Blueprint.Model.ApiResource
            {
                public override string Object => "test-resource-2";
                public string Id { get; set; } = "";
            }

            [RootLink("/host-aware/legacy-resource-updated")]
            public partial class LegacyResourceUpdatedOperation : ICommand<ResourceUpdated<TestResource2>> {}
            """);

        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();
        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP108");
    }
}
