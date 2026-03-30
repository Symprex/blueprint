using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorSerializerContextCoverageTests
{
    [Fact]
    public void When_http_descriptor_is_configured_then_serializer_context_coverage_feature_data_matches_response_body_types()
    {
        var data = HttpResponseMetadataQueryOp.Configure().GetFeatureData<OperationSerializerContextCoverageFeatureData>();

        data.Should().NotBeNull();
        data!.BodyTypeNames.Should().Equal(
            "string",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "Microsoft.AspNetCore.Mvc.ProblemDetails");
    }
}
