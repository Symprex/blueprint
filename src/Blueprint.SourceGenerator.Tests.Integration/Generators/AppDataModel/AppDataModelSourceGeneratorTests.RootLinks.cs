using Blueprint.Model;
using Blueprint.Model.Attributes;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.AppDataModel;

public class AppDataModelSourceGeneratorRootLinkTests
{
    [Fact]
    public void When_RootLink_on_operation_with_no_parameters_then_included()
    {
        var registeredOperation = new IntegrationTestBlueprintApp()
            .FindOperation(typeof(RootLinkOperationWithNoParameters));

        registeredOperation.Should().NotBeNull();
        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/test");
    }

    [Fact]
    public void When_RootLink_on_operation_with_parameters_then_included()
    {
        var registeredOperation = new IntegrationTestBlueprintApp()
            .FindOperation(typeof(RootLinkOperationWithParameter));

        registeredOperation.Should().NotBeNull();
        registeredOperation.Urls.Should().HaveCount(1);
        registeredOperation.Urls.First().UrlFormat.Should().Be("/test/{Id}");
    }
}

[RootLink("/test")]
public partial class RootLinkOperationWithNoParameters : IOperation;

[RootLink("/test/{Id}")]
public partial class RootLinkOperationWithParameter : IOperation
{
    public required string Id { get; init; }
}
