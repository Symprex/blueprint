using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorRootLinkTests
{
    [Fact]
    public void When_RootLink_on_operation_with_no_parameters_then_included()
    {
        var registeredOperation = RootLinkOperationWithNoParameters.Configure();

        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/test");
    }

    [Fact]
    public void When_RootLink_on_operation_with_parameters_then_included()
    {
        var registeredOperation = RootLinkOperationWithParameter.Configure();

        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/test/{Id}");
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/test")]
public partial class RootLinkOperationWithNoParameters : IOperation
{
    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/test/{Id}")]
public partial class RootLinkOperationWithParameter : IOperation
{
    public required string Id { get; init; }

    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}
