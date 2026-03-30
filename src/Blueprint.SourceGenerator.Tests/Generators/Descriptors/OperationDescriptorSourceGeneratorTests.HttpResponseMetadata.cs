using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsHttpResponseMetadata
{
    [Fact]
    public void When_http_operation_declares_success_contract_and_default_failure_outcomes_then_generated_descriptor_sets_http_response_metadata_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [Blueprint.Model.Attributes.RootLink("/openapi/http-response-metadata")]
            public partial class HttpResponseMetadataOperation : IQuery<OkResult<string>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("HttpResponseMetadataOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("new global::Blueprint.Model.OperationHttpResponseMetadataFeatureData(");
        descriptorSource.Should().Contain("new global::Blueprint.Model.OperationHttpResponseMetadata(200");
        descriptorSource.Should().Contain("new global::Blueprint.Model.OperationHttpResponseMetadata(422");
        descriptorSource.Should().Contain("new global::Blueprint.Model.OperationHttpResponseMetadata(500");
        descriptorSource.Should().Contain("\"Blueprint.Runtime.Exec.OkResult<string>\"");
        descriptorSource.Should().Contain("\"Blueprint.Runtime.Exec.ValidationFailedResult\"");
        descriptorSource.Should().Contain("\"Blueprint.Runtime.Exec.UnhandledExceptionResult\"");
    }

    [Fact]
    public void When_http_operation_declares_success_contract_and_default_failure_outcomes_then_generated_descriptor_sets_http_response_body_type_metadata()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [Blueprint.Model.Attributes.RootLink("/openapi/http-response-metadata")]
            public partial class HttpResponseBodyTypeOperation : IQuery<OkResult<string>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("HttpResponseBodyTypeOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("\"string\"");
        descriptorSource.Should().Contain("\"Microsoft.AspNetCore.Mvc.ValidationProblemDetails\"");
        descriptorSource.Should().Contain("\"Microsoft.AspNetCore.Mvc.ProblemDetails\"");
    }
}
