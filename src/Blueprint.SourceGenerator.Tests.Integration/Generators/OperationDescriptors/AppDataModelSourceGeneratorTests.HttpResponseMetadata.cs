using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorHttpResponseMetadataTests
{
    [Fact]
    public void When_http_descriptor_is_configured_then_http_response_metadata_feature_data_matches_success_and_failure_outcomes()
    {
        var data = HttpResponseMetadataQueryOp.Configure().GetFeatureData<OperationHttpResponseMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.Responses.Select(static response => response.StatusCode)
            .Should()
            .Equal(200, 422, 500);
        data.Responses[0].ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.OkResult<string>");
        data.Responses[1].ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.ValidationFailedResult");
        data.Responses[2].ResultTypeNames.Should().Equal("Blueprint.Runtime.Exec.UnhandledExceptionResult");
    }
}

[Blueprint.Model.Attributes.RootLink("/openapi/http-response-metadata")]
public partial class HttpResponseMetadataQueryOp : IQuery<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}
