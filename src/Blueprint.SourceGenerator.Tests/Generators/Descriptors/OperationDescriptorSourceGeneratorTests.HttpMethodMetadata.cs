using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsHttpMethodMetadataTests
{
    [Fact]
    public void When_http_operation_declares_blank_explicit_method_then_generator_reports_a_compile_time_diagnostic()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/http-method/blank-explicit-query")]
            [HttpMethod("   ")]
            public partial class BlankExplicitMethodQueryOperation : IQuery<string> {}
            """);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Contain("BPOA001");
    }

    [Fact]
    public void When_http_query_has_no_explicit_method_then_generated_descriptor_sets_default_get_http_method_metadata()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/http-method/default-query")]
            public partial class DefaultQueryOperation : IQuery<string> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("DefaultQueryOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.HttpGet);");
    }

    [Fact]
    public void When_http_command_has_no_explicit_method_then_generated_descriptor_sets_default_post_http_method_metadata()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/http-method/default-command")]
            public partial class DefaultCommandOperation : ICommand {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("DefaultCommandOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.HttpPost);");
    }

    [Fact]
    public void When_http_command_declares_explicit_get_attribute_then_generated_descriptor_uses_explicit_http_method_metadata()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/http-method/explicit-get-command")]
            [HttpGet]
            public partial class ExplicitGetCommandOperation : ICommand {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("ExplicitGetCommandOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.HttpGet);");
    }

    [Fact]
    public void When_http_operation_declares_multiple_explicit_methods_then_generated_descriptor_normalizes_deduplicates_and_orders_them()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/http-method/multi-explicit-query")]
            [HttpMethod("post")]
            [HttpGet]
            [HttpMethod("POST")]
            public partial class MultiExplicitMethodQueryOperation : IQuery<string> {}
            """);

        result.Diagnostics.Select(static d => d.Id).Should().Contain("BPOA002",
            "conflicting HTTP methods (GET + POST) should produce a warning");

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("MultiExplicitMethodQueryOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain(
            "this.SetFeatureData(new global::Blueprint.Model.OperationHttpMethodMetadataFeatureData(global::System.Collections.Immutable.ImmutableArray.Create(\"GET\", \"POST\")));");
    }

    [Fact]
    public void When_http_operation_declares_conflicting_methods_then_reports_BPOA002_warning()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/http-method/conflicting")]
            [HttpGet]
            [HttpPost]
            public partial class ConflictingMethodsOperation : IQuery<string> {}
            """);

        var bpoa002 = result.Diagnostics.FirstOrDefault(static d => d.Id == "BPOA002");
        bpoa002.Should().NotBeNull("operation with both [HttpGet] and [HttpPost] should trigger BPOA002");
        bpoa002!.Severity.Should().Be(DiagnosticSeverity.Warning);
        bpoa002.GetMessage().Should().Contain("GET").And.Contain("POST");
    }

    [Fact]
    public void When_http_operation_declares_duplicate_same_method_then_no_BPOA002_warning()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            [RootLink("/http-method/duplicate-same")]
            [HttpPost]
            [HttpMethod("POST")]
            public partial class DuplicateSameMethodOperation : ICommand {}
            """);

        result.Diagnostics.Where(static d => d.Id == "BPOA002").Should().BeEmpty(
            "duplicate [HttpPost] attributes resolve to one method after dedup — no conflict");
    }
}
