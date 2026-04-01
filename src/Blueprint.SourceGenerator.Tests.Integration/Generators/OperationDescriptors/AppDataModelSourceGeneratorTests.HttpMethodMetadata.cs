using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorHttpMethodMetadataTests
{
    [Fact]
    public void When_http_query_descriptor_is_configured_then_http_method_metadata_defaults_to_get()
    {
        var data = HttpResponseMetadataQueryOp.Configure().GetFeatureData<OperationHttpMethodMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.HttpMethods.Should().Equal("GET");
    }

    [Fact]
    public void When_http_command_descriptor_is_configured_then_http_method_metadata_defaults_to_post()
    {
        var data = HttpMethodConventionCommandOp.Configure().GetFeatureData<OperationHttpMethodMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.HttpMethods.Should().Equal("POST");
    }

    [Fact]
    public void When_http_command_declares_explicit_http_method_then_http_method_metadata_uses_the_attribute_value()
    {
        var data = HttpMethodExplicitGetCommandOp.Configure().GetFeatureData<OperationHttpMethodMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.HttpMethods.Should().Equal("GET");
    }

    [Fact]
    public void When_http_operation_declares_multiple_explicit_methods_then_http_method_metadata_is_normalized_deduplicated_and_sorted()
    {
        var data = HttpMethodMultipleExplicitQueryOp.Configure().GetFeatureData<OperationHttpMethodMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.HttpMethods.Should().Equal("GET", "POST");
    }
}

[RootLink("/openapi/http-method-convention-command")]
public partial class HttpMethodConventionCommandOp : ICommand<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}

[RootLink("/openapi/http-method-explicit-get-command")]
[HttpGet]
public partial class HttpMethodExplicitGetCommandOp : ICommand<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}

[RootLink("/openapi/http-method-multi-explicit-query")]
[HttpMethod("post")]
[HttpGet]
[HttpMethod("POST")]
public partial class HttpMethodMultipleExplicitQueryOp : IQuery<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}
