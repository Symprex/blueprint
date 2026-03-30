using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsSerializerContextCoverage
{
    [Fact]
    public void When_http_operation_declares_body_bearing_response_types_then_generated_descriptor_sets_serializer_context_coverage_feature_data()
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile("""
            using Blueprint.Runtime.Exec;

            [Blueprint.Model.Attributes.RootLink("/openapi/http-response-metadata")]
            public partial class HttpResponseSerializerCoverageOperation : IQuery<OkResult<string>> {}
            """);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("HttpResponseSerializerCoverageOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("new global::Blueprint.Model.OperationSerializerContextCoverageFeatureData(");
        descriptorSource.Should().Contain("\"string\"");
        descriptorSource.Should().Contain("\"Microsoft.AspNetCore.Mvc.ValidationProblemDetails\"");
        descriptorSource.Should().Contain("\"Microsoft.AspNetCore.Mvc.ProblemDetails\"");
    }
}
