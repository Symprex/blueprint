using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorSelfLinksTests
{
    [Fact]
    public void When_SelfLink_on_operation_with_no_parameters_then_included()
    {
        var registeredOperation = SelfLinkOperationWithNoParameters.Configure();

        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/self/test");

        registeredOperation.Links.Should().HaveCount(1);
        registeredOperation.Links.First().UrlFormat.Should().Be("/self/test");
        registeredOperation.Links.First().Rel.Should().Be("self");
    }

    [Fact]
    public void When_SelfLink_on_operation_with_parameters_then_included()
    {
        var registeredOperation = SelfLinkOperationWithParameter.Configure();

        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/self/test/{Id}");

        registeredOperation.Links.Should().HaveCount(1);
        registeredOperation.Links.First().UrlFormat.Should().Be("/self/test/{Id}");
        registeredOperation.Links.First().Rel.Should().Be("self");
    }

    [Fact]
    public void When_SelfLink_on_operation_with_parameters_with_matching_resource_parameter_then_included()
    {
        var registeredOperation = SelfLinkOperationWithMatchingParameter.Configure();

        registeredOperation.Should().NotBeNull();
        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/self/matching/{Id}");

        registeredOperation.Links.Should().HaveCount(1);
        registeredOperation.Links.First().UrlFormat.Should().Be("/self/matching/{Id}");
        registeredOperation.Links.First().Rel.Should().Be("self");
    }

    [Fact]
    public void When_SelfLink_on_operation_with_parameters_with_matching_mapped_then_included()
    {
        var registeredOperation = SelfLinkOperationWithMappedParameter.Configure();

        registeredOperation.Should().NotBeNull();
        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/self/mapped/{RouteId}");

        registeredOperation.Links.Should().HaveCount(1);
        registeredOperation.Links.First().UrlFormat.Should().Be("/self/mapped/{RouteId}");
        registeredOperation.Links.First().Rel.Should().Be("self");
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.SelfLink(typeof(SelfLinkOperationWithNoParametersApiResource), "/self/test")]
public partial class SelfLinkOperationWithNoParameters : IOperation
{
    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}

public partial class SelfLinkOperationWithNoParametersApiResource : ApiResource;

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.SelfLink(typeof(SelfLinkOperationWithParameterApiResource), "/self/test/{Id}")]
public partial class SelfLinkOperationWithParameter : IOperation
{
    public required string Id { get; init; }

    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}

public partial class SelfLinkOperationWithParameterApiResource : ApiResource
{
    public string Id { get; init; } = null!;
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.SelfLink(typeof(SelfLinkOperationWithMatchingParameterApiResource), "/self/matching/{Id}")]
public partial class SelfLinkOperationWithMatchingParameter : IOperation
{
    public required string Id { get; init; }

    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}

public partial class SelfLinkOperationWithMatchingParameterApiResource : ApiResource
{
    public string Id { get; init; } = null!;
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.SelfLink(typeof(SelfLinkOperationWithMappedParameterApiResource), "/self/mapped/{RouteId:Id}")]
public partial class SelfLinkOperationWithMappedParameter : IOperation
{
    public required string RouteId { get; init; }

    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}

public partial class SelfLinkOperationWithMappedParameterApiResource : ApiResource
{
    public string Id { get; init; } = null!;
}
