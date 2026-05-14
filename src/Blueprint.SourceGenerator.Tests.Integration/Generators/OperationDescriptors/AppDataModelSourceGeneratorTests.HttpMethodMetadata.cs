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
    public void When_http_operation_declares_duplicate_same_method_then_http_method_metadata_is_normalized_and_deduplicated()
    {
        var data = HttpMethodDuplicateExplicitQueryOp.Configure().GetFeatureData<OperationHttpMethodMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.HttpMethods.Should().Equal("POST");
    }

    [Fact]
    public void When_http_operation_declares_multiple_distinct_methods_then_http_method_metadata_preserves_all()
    {
        var data = HttpMethodMultipleDistinctQueryOp.Configure().GetFeatureData<OperationHttpMethodMetadataFeatureData>();

        data.Should().NotBeNull();
        data!.HttpMethods.Should().Equal("GET", "POST");
    }
}

[AllowAnonymous]
[RootLink("/openapi/http-method-convention-command")]
public partial class HttpMethodConventionCommandOp : ICommand<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}

[AllowAnonymous]
[RootLink("/openapi/http-method-explicit-get-command")]
[HttpGet]
public partial class HttpMethodExplicitGetCommandOp : ICommand<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}

[AllowAnonymous]
[RootLink("/openapi/http-method-duplicate-explicit-query")]
[HttpMethod("post")]
[HttpPost]
[HttpMethod("POST")]
public partial class HttpMethodDuplicateExplicitQueryOp : IQuery<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}

[AllowAnonymous]
[RootLink("/openapi/http-method-multi-distinct-query")]
[HttpMethod("post")]
[HttpGet]
[HttpMethod("POST")]
public partial class HttpMethodMultipleDistinctQueryOp : IQuery<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}
