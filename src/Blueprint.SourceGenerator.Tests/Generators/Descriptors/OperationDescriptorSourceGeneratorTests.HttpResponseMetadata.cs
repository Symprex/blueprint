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
        descriptorSource.Should().Contain(".AddRange(global::Blueprint.Model.WellKnownFeatureData.CommonFailureResponses)");
        descriptorSource.Should().Contain("\"Blueprint.Runtime.Exec.OkResult<string>\"");
    }

    [Fact]
    public void When_http_operation_has_must_be_authenticated_then_generated_descriptor_includes_401_response()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [Blueprint.Model.Attributes.RootLink("/auth-op")]
            [Blueprint.Model.Attributes.MustBeAuthenticated]
            public partial class AuthenticatedGetOperation : IQuery<OkResult<string>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("AuthenticatedGetOperationDescriptor"));

        // Success + [401, 422, 500] tail → AddRange(AuthenticatedFailureResponses)
        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("AuthenticatedFailureResponses");
    }

    [Fact]
    public void When_http_command_has_body_binding_then_generated_descriptor_includes_400_and_415_responses()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [Blueprint.Model.Attributes.RootLink("/body-op")]
            public partial class BodyBoundCommand : ICommand
            {
                public string Name { get; set; } = string.Empty;
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("BodyBoundCommandDescriptor"));

        // Failures-only [400, 415, 422, 500] → BodyBindingFailureResponsesFeatureData constant
        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("BodyBindingFailureResponsesFeatureData");
    }

    [Fact]
    public void When_http_command_has_body_binding_and_must_be_authenticated_then_generated_descriptor_includes_400_401_and_415_responses()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [Blueprint.Model.Attributes.RootLink("/auth-body-op")]
            [Blueprint.Model.Attributes.MustBeAuthenticated]
            public partial class AuthenticatedBodyBoundCommand : ICommand
            {
                public string Name { get; set; } = string.Empty;
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("AuthenticatedBodyBoundCommandDescriptor"));

        // Failures-only [400, 401, 415, 422, 500] → AuthenticatedBodyBindingFailureResponsesFeatureData constant
        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("AuthenticatedBodyBindingFailureResponsesFeatureData");
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
